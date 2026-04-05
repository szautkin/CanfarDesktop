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
    """Handle Jupyter-style magic commands. Returns list of outputs, or None if not a magic."""
    import subprocess as _sp

    stripped = code.strip()

    # %pip install <packages>
    if stripped.startswith("%pip ") or stripped.startswith("!pip "):
        args = stripped.split(None, 1)[1]  # everything after %pip
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
    if stripped.startswith("%conda ") or stripped.startswith("!conda "):
        args = stripped.split(None, 1)[1]
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

    # !shell command
    if stripped.startswith("!"):
        shell_cmd = stripped[1:].strip()
        try:
            result = _sp.run(shell_cmd, shell=True, capture_output=True, text=True, timeout=120)
            outputs = []
            if result.stdout:
                outputs.append({"type": "stream", "name": "stdout", "text": result.stdout})
            if result.stderr:
                outputs.append({"type": "stream", "name": "stderr", "text": result.stderr})
            return outputs
        except Exception as e:
            return [{"type": "error", "ename": type(e).__name__, "evalue": str(e), "traceback": [str(e)]}]

    # %matplotlib inline (already set up in harness init, just acknowledge)
    if stripped in ("%matplotlib inline", "%matplotlib"):
        return [{"type": "stream", "name": "stdout", "text": "Matplotlib backend: Agg (inline)\n"}]

    # Not a magic command
    return None


def execute_code(code, exec_count):
    """Execute user code, capturing stdout, stderr, display data, and errors."""
    send_status("busy")
    outputs = []
    success = True

    # Normalize line endings (Windows TextBox sends \r\n)
    code = code.replace("\r\n", "\n").replace("\r", "\n")

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
            # Debug: log received code to temp file
            import tempfile, os
            debug_path = os.path.join(tempfile.gettempdir(), "canfar_kernel_debug.log")
            with open(debug_path, "a", encoding="utf-8") as df:
                df.write(f"--- exec_count={exec_count} len={len(code)} ---\n")
                df.write(repr(code) + "\n")
                df.write(code + "\n")
                df.write("--- end ---\n")
            if code.strip():
                execute_code(code, exec_count)
            else:
                # Empty code — send immediate reply
                send({"type": "execute_reply", "exec_count": exec_count, "success": True})
                send_boundary()


if __name__ == "__main__":
    main()
