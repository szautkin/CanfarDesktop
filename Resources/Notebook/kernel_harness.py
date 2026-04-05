"""
Jupiter Kernel Harness — Python execution subprocess for CanfarDesktop.

Protocol: reads JSON requests from stdin, writes JSON responses to stdout.
Each response is terminated by a sentinel line so the host can split messages.

Requests:  {"type": "execute", "code": "...", "exec_count": N}
           {"type": "interrupt"}
           {"type": "quit"}

Responses: {"type": "stream", "name": "stdout"|"stderr", "text": "..."}
           {"type": "execute_result", "data": {"text/plain": "..."}, "exec_count": N}
           {"type": "display_data", "data": {"image/png": "base64...", "text/plain": "..."}}
           {"type": "error", "ename": "...", "evalue": "...", "traceback": [...]}
           {"type": "status", "state": "idle"|"busy"}
           {"type": "execute_reply", "exec_count": N, "success": true|false}

Sentinel: \\x04__CANFAR_EXEC_BOUNDARY__\\x04
"""

import sys
import io
import json
import traceback
import base64

SENTINEL = "\x04__CANFAR_EXEC_BOUNDARY__\x04"

# Force UTF-8 on Windows
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")
if hasattr(sys.stdin, "reconfigure"):
    sys.stdin.reconfigure(encoding="utf-8")


def send(msg):
    """Send a JSON message followed by the sentinel."""
    sys.stdout.write(json.dumps(msg) + "\n")
    sys.stdout.flush()


def send_boundary():
    """Send the sentinel boundary after all outputs for one execution."""
    sys.stdout.write(SENTINEL + "\n")
    sys.stdout.flush()


def send_status(state):
    send({"type": "status", "state": state})


# User namespace for exec()
_user_ns = {"__name__": "__main__", "__builtins__": __builtins__}

# Try to set up matplotlib inline backend
try:
    import matplotlib
    matplotlib.use("Agg")
except ImportError:
    pass

# Colab compatibility: rewrite /content/ paths to working directory.
# Many Colab notebooks hardcode paths like /content/repo/file.
import os as _os
_colab_prefix = "/content/"
_colab_replacement = _os.getcwd().replace("\\", "/") + "/"  # absolute path as replacement

# Mock google.colab module so Colab notebooks don't crash on import
import types as _types
_mock_colab = _types.ModuleType("google.colab")
_mock_colab.output = _types.ModuleType("google.colab.output")
_mock_colab.output.enable_custom_widget_manager = lambda: None
_mock_google = _types.ModuleType("google")
_mock_google.colab = _mock_colab
sys.modules["google"] = _mock_google
sys.modules["google.colab"] = _mock_colab
sys.modules["google.colab.output"] = _mock_colab.output


def _capture_display_data(obj):
    """Hook for IPython-style display. Returns mime dict or None."""
    # Check for matplotlib figure
    try:
        import matplotlib.pyplot as plt
        from matplotlib.figure import Figure
        if isinstance(obj, Figure):
            buf = io.BytesIO()
            obj.savefig(buf, format="png", bbox_inches="tight")
            buf.seek(0)
            b64 = base64.b64encode(buf.read()).decode("ascii")
            plt.close(obj)
            return {"image/png": b64, "text/plain": repr(obj)}
    except ImportError:
        pass
    return None


def _handle_magic(code, exec_count):
    """Handle cells containing any mix of magic/shell lines and regular Python.

    Lines are processed top-to-bottom:
      - magic/shell lines  → subprocess
      - regular code lines → accumulated then exec()'d as a block

    Returns a list of output dicts, or None if the cell contains no magic at all.
    """
    import subprocess as _sp

    MAGIC_PREFIXES = ("!", "%pip ", "%pip\t", "%conda ", "%conda\t", "%matplotlib")

    lines = code.replace("\r\n", "\n").replace("\r", "\n").split("\n")

    def is_magic(line):
        s = line.strip()
        return bool(s) and any(s.startswith(p) for p in MAGIC_PREFIXES)

    # If no magic lines at all, return None → regular execute_code handles it
    if not any(is_magic(l) for l in lines):
        return None

    outputs = []
    pending_code = []

    def flush_code():
        """exec() any accumulated regular Python lines, capture output."""
        nonlocal pending_code
        block = "\n".join(pending_code).strip()
        pending_code = []
        if not block:
            return
        # Apply Colab path rewrite to code blocks too
        if _colab_prefix in block:
            block = block.replace(_colab_prefix, _colab_replacement)
        import io as _io
        old_out, old_err = sys.stdout, sys.stderr
        co, ce = _io.StringIO(), _io.StringIO()
        try:
            sys.stdout, sys.stderr = co, ce
            try:
                result = eval(compile(block, "<cell>", "eval"), _user_ns)
                sys.stdout, sys.stderr = old_out, old_err
                o = co.getvalue()
                if o:
                    outputs.append({"type": "stream", "name": "stdout", "text": o})
                if result is not None:
                    outputs.append({
                        "type": "execute_result",
                        "data": {"text/plain": repr(result)},
                        "exec_count": exec_count,
                    })
            except SyntaxError:
                exec(compile(block, "<cell>", "exec"), _user_ns)
                sys.stdout, sys.stderr = old_out, old_err
                o = co.getvalue()
                if o:
                    outputs.append({"type": "stream", "name": "stdout", "text": o})
        except Exception as e:
            sys.stdout, sys.stderr = old_out, old_err
            # Filter harness frames from traceback — show only user code
            tb = e.__traceback__
            while tb is not None and "kernel_harness" in (tb.tb_frame.f_code.co_filename or ""):
                tb = tb.tb_next
            tb_lines = traceback.format_exception(type(e), e, tb or e.__traceback__)
            outputs.append({
                "type": "error",
                "ename": type(e).__name__,
                "evalue": str(e),
                "traceback": tb_lines,
            })
        finally:
            sys.stdout, sys.stderr = old_out, old_err
            e2 = ce.getvalue()
            if e2:
                outputs.append({"type": "stream", "name": "stderr", "text": e2})

    for line in lines:
        s = line.strip()
        if not s:
            # Preserve blank lines inside code blocks; skip between magic lines
            if pending_code:
                pending_code.append(line)
            continue

        if is_magic(s):
            flush_code()  # exec any accumulated code before running magic
            result = _handle_single_magic(s, _sp)
            if result:
                outputs.extend(result)
        else:
            pending_code.append(line)

    flush_code()  # trailing regular code after the last magic line

    return outputs


def _handle_single_magic(line, _sp):
    """Handle a single magic/shell command line."""
    # %pip install <packages>
    if line.startswith("%pip ") or line.startswith("!pip "):
        args = line.split(None, 1)[1]
        cmd = [sys.executable, "-m", "pip"] + args.split()
        try:
            result = _sp.run(cmd, capture_output=True, text=True, timeout=300)
            outputs = []
            if result.stdout:
                outputs.append({"type": "stream", "name": "stdout", "text": result.stdout})
            if result.stderr:
                outputs.append({"type": "stream", "name": "stderr", "text": result.stderr})
            return outputs
        except Exception as e:
            return [{"type": "error", "ename": type(e).__name__, "evalue": str(e), "traceback": [str(e)]}]

    # %conda install <packages>
    if line.startswith("%conda ") or line.startswith("!conda "):
        args = line.split(None, 1)[1]
        cmd = ["conda"] + args.split() + ["-y"]
        try:
            result = _sp.run(cmd, capture_output=True, text=True, timeout=600)
            outputs = []
            if result.stdout:
                outputs.append({"type": "stream", "name": "stdout", "text": result.stdout})
            if result.stderr:
                outputs.append({"type": "stream", "name": "stderr", "text": result.stderr})
            return outputs
        except Exception as e:
            return [{"type": "error", "ename": type(e).__name__, "evalue": str(e), "traceback": [str(e)]}]

    # !shell command — stream output line-by-line for long-running commands
    if line.startswith("!"):
        shell_cmd = line[1:].strip()
        try:
            proc = _sp.Popen(
                shell_cmd, shell=True,
                stdout=_sp.PIPE, stderr=_sp.STDOUT,
                text=True, bufsize=1,
            )
            outputs = []
            collected = []
            for out_line in proc.stdout:
                collected.append(out_line)
            proc.wait(timeout=120)
            text = "".join(collected)
            if text:
                outputs.append({"type": "stream", "name": "stdout", "text": text})
            if proc.returncode != 0:
                outputs.append({"type": "stream", "name": "stderr",
                                "text": f"[exit code {proc.returncode}]\n"})
            return outputs
        except _sp.TimeoutExpired:
            proc.kill()
            return [{"type": "error", "ename": "TimeoutError",
                     "evalue": f"Command timed out after 120s: {shell_cmd}",
                     "traceback": [f"TimeoutError: {shell_cmd}"]}]
        except Exception as e:
            return [{"type": "error", "ename": type(e).__name__, "evalue": str(e), "traceback": [str(e)]}]

    # %matplotlib [backend]
    if line.startswith("%matplotlib"):
        backend = line[len("%matplotlib"):].strip() or "inline"
        return [{"type": "stream", "name": "stdout", "text": f"Matplotlib backend: {backend} (using Agg)\n"}]

    return None


def execute_code(code, exec_count):
    """Execute user code, capturing stdout, stderr, display data, and errors."""
    send_status("busy")
    outputs = []
    success = True

    # Normalize line endings (Windows TextBox sends \r\n)
    code = code.replace("\r\n", "\n").replace("\r", "\n")

    # Colab compatibility: rewrite /content/ paths to working directory
    if _colab_prefix in code:
        code = code.replace(_colab_prefix, _colab_replacement)

    # Handle magic commands (%pip, %conda, !shell)
    magic_result = _handle_magic(code, exec_count)
    if magic_result is not None:
        for out in magic_result:
            send(out)
        send({"type": "execute_reply", "exec_count": exec_count, "success": True})
        send_status("idle")
        send_boundary()
        return

    # Redirect stdout/stderr
    old_stdout, old_stderr = sys.stdout, sys.stderr
    captured_out = io.StringIO()
    captured_err = io.StringIO()

    try:
        sys.stdout = captured_out
        sys.stderr = captured_err

        # Try to get a displayable result from the last expression
        result = None
        try:
            # Try to compile as eval (single expression)
            compiled = compile(code, "<cell>", "eval")
            result = eval(compiled, _user_ns)
        except SyntaxError:
            # Not an expression — execute as statements
            exec(compile(code, "<cell>", "exec"), _user_ns)

    except Exception as e:
        success = False
        # Filter traceback to show only user code frames, not harness internals
        tb = e.__traceback__
        # Skip frames inside this file
        while tb is not None and "kernel_harness" in (tb.tb_frame.f_code.co_filename or ""):
            tb = tb.tb_next
        if tb is None:
            tb = e.__traceback__
        clean_lines = traceback.format_exception(type(e), e, tb)
        outputs.append({
            "type": "error",
            "ename": type(e).__name__,
            "evalue": str(e),
            "traceback": clean_lines,
        })
    finally:
        sys.stdout = old_stdout
        sys.stderr = old_stderr

    # Flush captured stdout
    stdout_text = captured_out.getvalue()
    if stdout_text:
        outputs.append({"type": "stream", "name": "stdout", "text": stdout_text})

    # Flush captured stderr
    stderr_text = captured_err.getvalue()
    if stderr_text:
        outputs.append({"type": "stream", "name": "stderr", "text": stderr_text})

    # Check for display data (matplotlib figures)
    try:
        import matplotlib.pyplot as plt
        figs = [plt.figure(i) for i in plt.get_fignums()]
        for fig in figs:
            display = _capture_display_data(fig)
            if display:
                outputs.append({"type": "display_data", "data": display})
        plt.close("all")
    except ImportError:
        pass

    # Expression result
    if result is not None and success:
        display = _capture_display_data(result)
        if display:
            outputs.append({"type": "display_data", "data": display})
        else:
            outputs.append({
                "type": "execute_result",
                "data": {"text/plain": repr(result)},
                "exec_count": exec_count,
            })

    # Send all outputs
    for out in outputs:
        send(out)

    send({"type": "execute_reply", "exec_count": exec_count, "success": success})
    send_status("idle")
    send_boundary()


def main():
    send_status("idle")
    send_boundary()

    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue

        # Check for interrupt sentinel (not JSON)
        if line == "__INTERRUPT__":
            # Currently executing code can't be interrupted this way (Python GIL).
            # This is handled by the host killing the process.
            continue

        try:
            msg = json.loads(line)
        except json.JSONDecodeError:
            continue

        msg_type = msg.get("type", "")

        if msg_type == "quit":
            break
        elif msg_type == "execute":
            code = msg.get("code", "")
            exec_count = msg.get("exec_count", 0)
            if code.strip():
                execute_code(code, exec_count)
            else:
                # Empty code — send immediate reply
                send({"type": "execute_reply", "exec_count": exec_count, "success": True})
                send_boundary()


if __name__ == "__main__":
    main()
