namespace CanfarDesktop.Services.Notebook;

using System.Diagnostics;
using System.Text;
using System.Text.Json;

/// <summary>
/// Manages a local Python subprocess for code execution.
/// Communicates via JSON over stdin/stdout with a sentinel delimiter.
///
/// Thread safety contract:
///   - _executionGate (SemaphoreSlim) serializes all process I/O
///   - _executionCts cancels in-flight reads before kill
///   - Sequence: cancel CTS → acquire gate → kill process → release gate
/// </summary>
public class LocalKernelService : IKernelService, IAsyncDisposable
{
    private const string Sentinel = "\x04__CANFAR_EXEC_BOUNDARY__\x04";

    private readonly IPythonDiscoveryService _pythonDiscovery;
    private Process? _process;
    private StreamWriter? _stdin;
    private int _executionCount;
    private volatile bool _disposed;
    private string? _harnessPath;
    private readonly object _lock = new();
    private readonly SemaphoreSlim _executionGate = new(1, 1);
    private CancellationTokenSource? _executionCts;

    public KernelState State { get; private set; } = KernelState.Dead;
    public event Action<KernelState>? StateChanged;
    public event Action<KernelOutput>? OutputReceived;

    public LocalKernelService(IPythonDiscoveryService pythonDiscovery)
    {
        _pythonDiscovery = pythonDiscovery;
    }

    public async Task StartAsync(string? workingDirectory = null)
    {
        lock (_lock)
        {
            if (State is KernelState.Idle or KernelState.Busy or KernelState.Starting) return;
            SetState(KernelState.Starting);
        }

        var pythonPath = await _pythonDiscovery.FindPythonAsync();
        NotebookLogger.Info($"Kernel start: Python={pythonPath ?? "NOT FOUND"}, WorkDir={workingDirectory}");
        if (pythonPath is null)
        {
            SetState(KernelState.Dead);
            throw new InvalidOperationException(
                "Python 3.8+ not found. Install Python from python.org or add it to PATH.");
        }

        _harnessPath = Path.Combine(Path.GetTempPath(), $"canfar_kernel_harness_{Environment.ProcessId}.py");
        await WriteHarnessAsync(_harnessPath);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = $"-u \"{_harnessPath}\"",
                WorkingDirectory = workingDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            psi.Environment["PYTHONIOENCODING"] = "utf-8";
            psi.Environment["PYTHONUNBUFFERED"] = "1";

            _process = Process.Start(psi);
            if (_process is null || _process.HasExited)
            {
                SetState(KernelState.Dead);
                throw new InvalidOperationException("Failed to start Python process.");
            }

            _stdin = _process.StandardInput;
            _stdin.AutoFlush = true;
            _executionCount = 0;

            _process.EnableRaisingEvents = true;
            _process.Exited += OnProcessExited;

            await ReadUntilBoundaryAsync(CancellationToken.None);
            SetState(KernelState.Idle);
        }
        catch
        {
            CleanupHarness();
            SetState(KernelState.Dead);
            throw;
        }
    }

    public async Task<ExecutionResult> ExecuteAsync(string code, CancellationToken ct = default)
    {
        if (State == KernelState.Dead || _stdin is null || _process is null)
        {
            SetState(KernelState.Dead);
            throw new InvalidOperationException("Kernel is not running. Restart the kernel and try again.");
        }

        await _executionGate.WaitAsync(ct);
        try
        {
            // Create a linked CTS so InterruptAsync can cancel this read
            _executionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var linked = _executionCts.Token;

            _executionCount++;
            var count = _executionCount;

            SetState(KernelState.Busy);

            code = code.Replace("\r\n", "\n").Replace("\r", "\n");
            NotebookLogger.Info($"Execute ({code.Length} chars): {code[..Math.Min(80, code.Length)]}...");

            var request = JsonSerializer.Serialize(new
            {
                type = "execute",
                code,
                exec_count = count
            });

            await _stdin!.WriteLineAsync(request);

            var outputs = await ReadUntilBoundaryAsync(linked);

            // If cancelled (interrupt), return empty result — don't process stale output
            if (linked.IsCancellationRequested)
            {
                SetState(KernelState.Dead);
                return new ExecutionResult { Success = false, ExecutionCount = count, Outputs = [] };
            }

            var success = true;
            var resultOutputs = new List<KernelOutput>();

            foreach (var msg in outputs)
            {
                var msgType = msg.GetProperty("type").GetString();

                switch (msgType)
                {
                    case "stream":
                        var output = new KernelOutput
                        {
                            OutputType = "stream",
                            StreamName = msg.GetProperty("name").GetString(),
                            Text = msg.GetProperty("text").GetString()
                        };
                        resultOutputs.Add(output);
                        OutputReceived?.Invoke(output);
                        break;

                    case "execute_result":
                        var dataDict = ExtractData(msg);
                        var execResult = new KernelOutput
                        {
                            OutputType = "execute_result",
                            Data = dataDict,
                            Text = dataDict.GetValueOrDefault("text/plain")
                        };
                        resultOutputs.Add(execResult);
                        OutputReceived?.Invoke(execResult);
                        break;

                    case "display_data":
                        var displayData = ExtractData(msg);
                        var displayOutput = new KernelOutput
                        {
                            OutputType = "display_data",
                            Data = displayData
                        };
                        resultOutputs.Add(displayOutput);
                        OutputReceived?.Invoke(displayOutput);
                        break;

                    case "error":
                        success = false;
                        var errorOutput = new KernelOutput
                        {
                            OutputType = "error",
                            Ename = msg.TryGetProperty("ename", out var en) ? en.GetString() : null,
                            Evalue = msg.TryGetProperty("evalue", out var ev) ? ev.GetString() : null,
                            Traceback = msg.TryGetProperty("traceback", out var tb)
                                ? tb.EnumerateArray().Select(e => e.GetString() ?? "").ToList()
                                : null
                        };
                        resultOutputs.Add(errorOutput);
                        OutputReceived?.Invoke(errorOutput);
                        break;

                    case "execute_reply":
                        if (msg.TryGetProperty("success", out var s))
                            success = s.GetBoolean();
                        break;
                }
            }

            SetState(KernelState.Idle);

            return new ExecutionResult
            {
                Success = success,
                ExecutionCount = count,
                Outputs = resultOutputs
            };
        }
        finally
        {
            _executionCts?.Dispose();
            _executionCts = null;
            _executionGate.Release();
        }
    }

    /// <summary>
    /// Interrupt: cancel in-flight read → wait for gate → kill → restart.
    /// This is the ONLY correct sequence on Windows (no SIGINT for child processes).
    /// </summary>
    public async Task InterruptAsync()
    {
        if (_process is null) return;

        NotebookLogger.Info("Interrupt requested — cancelling execution, then kill+restart");

        var workDir = _process?.StartInfo?.WorkingDirectory;

        // Step 1: Cancel the in-flight ReadLineAsync — unblocks ExecuteAsync
        _executionCts?.Cancel();

        // Step 2: Wait for ExecuteAsync to release the semaphore
        await _executionGate.WaitAsync();
        try
        {
            // Step 3: NOW safe to kill — nobody is reading stdout
            KillProcess();
        }
        finally
        {
            _executionGate.Release();
        }

        // Step 4: Restart
        _executionCount = 0;
        try
        {
            await StartAsync(workDir);
            NotebookLogger.Info("Kernel restarted after interrupt");
        }
        catch (Exception ex)
        {
            NotebookLogger.Error("Kernel restart after interrupt failed", ex);
        }
    }

    public async Task RestartAsync(string? workingDirectory = null)
    {
        _executionCts?.Cancel();

        await _executionGate.WaitAsync();
        try
        {
            KillProcess();
        }
        finally
        {
            _executionGate.Release();
        }

        _executionCount = 0;
        await StartAsync(workingDirectory);
    }

    public async Task ShutdownAsync()
    {
        if (_process is null) return;

        // Cancel any in-flight execution
        _executionCts?.Cancel();

        // Wait for ExecuteAsync to finish
        await _executionGate.WaitAsync();
        try
        {
            var proc = _process;
            if (proc is null) return;

            proc.Exited -= OnProcessExited;

            try
            {
                if (!proc.HasExited && _stdin is not null)
                {
                    // Send quit and give 500ms for clean exit — don't block 3 seconds
                    await _stdin.WriteLineAsync(JsonSerializer.Serialize(new { type = "quit" }));
                    await Task.Delay(500);
                }
            }
            catch { /* stdin write may fail if process already exiting */ }

            // Kill + dispose on background thread (fast, non-blocking)
            try { proc.Exited -= OnProcessExited; } catch { }
            Task.Run(() =>
            {
                try { if (!proc.HasExited) proc.Kill(); } catch { }
                try { proc.Dispose(); } catch { }
            });

            _process = null;
            _stdin = null;
            SetState(KernelState.Dead);
            CleanupHarness();
        }
        finally
        {
            _executionGate.Release();
        }
    }

    /// <summary>
    /// Kill the process immediately. MUST only be called while holding _executionGate.
    /// </summary>
    private void KillProcess()
    {
        var proc = _process;
        _process = null;
        _stdin = null;

        if (proc is null) return;

        try { proc.Exited -= OnProcessExited; } catch { }

        // Kill on a background thread to avoid blocking UI.
        // Kill(false) = just the process, not the entire tree (fast).
        // Dispose on background too since it waits for handle cleanup.
        Task.Run(() =>
        {
            try { if (!proc.HasExited) proc.Kill(); } catch { }
            try { proc.Dispose(); } catch { }
        });

        SetState(KernelState.Dead);
        CleanupHarness();
    }

    /// <summary>
    /// Read stdout lines until the sentinel boundary or cancellation/error.
    /// Never throws — returns partial results on cancel/IOException.
    /// </summary>
    private async Task<List<JsonElement>> ReadUntilBoundaryAsync(CancellationToken ct)
    {
        var messages = new List<JsonElement>();
        var proc = _process;
        if (proc is null) return messages;

        var reader = proc.StandardOutput;

        try
        {
            while (!ct.IsCancellationRequested && !_disposed)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line is null)
                {
                    SetState(KernelState.Dead);
                    break;
                }

                if (line == Sentinel) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    var doc = JsonDocument.Parse(line);
                    messages.Add(doc.RootElement.Clone());
                }
                catch (JsonException)
                {
                    messages.Add(JsonDocument.Parse(
                        JsonSerializer.Serialize(new { type = "stream", name = "stdout", text = line + "\n" })
                    ).RootElement.Clone());
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Interrupt cancelled the read — expected
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ObjectDisposedException)
        {
            // Process died while we were reading — expected during kill
            SetState(KernelState.Dead);
        }

        return messages;
    }

    private static Dictionary<string, string> ExtractData(JsonElement msg)
    {
        var dict = new Dictionary<string, string>();
        if (msg.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in data.EnumerateObject())
            {
                dict[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                    ? prop.Value.GetString() ?? ""
                    : prop.Value.ToString();
            }
        }
        return dict;
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (_disposed) return;

        int exitCode = -1;
        try
        {
            var proc = _process;
            if (proc is not null)
                exitCode = proc.ExitCode;
        }
        catch { }

        NotebookLogger.Warn($"Kernel process exited unexpectedly (exit code: {exitCode})");
        SetState(KernelState.Dead);
        CleanupHarness();
    }

    private void SetState(KernelState newState)
    {
        if (State == newState) return;
        State = newState;
        StateChanged?.Invoke(newState);
    }

    private void CleanupHarness()
    {
        if (_harnessPath is not null && File.Exists(_harnessPath))
        {
            try { File.Delete(_harnessPath); }
            catch { }
        }
    }

    private static async Task WriteHarnessAsync(string path)
    {
        var assembly = System.Reflection.Assembly.GetExecutingAssembly();
        var resourceName = "CanfarDesktop.Resources.Notebook.kernel_harness.py";

        await using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is not null)
        {
            await using var fileStream = File.Create(path);
            await stream.CopyToAsync(fileStream);
        }
        else
        {
            await File.WriteAllTextAsync(path, GetFallbackHarness());
        }
    }

    private static string GetFallbackHarness() => """
        import sys, io, json, traceback
        SENTINEL = "\x04__CANFAR_EXEC_BOUNDARY__\x04"
        _ns = {"__name__": "__main__", "__builtins__": __builtins__}
        if hasattr(sys.stdout, "reconfigure"): sys.stdout.reconfigure(encoding="utf-8")
        if hasattr(sys.stdin, "reconfigure"): sys.stdin.reconfigure(encoding="utf-8")
        def send(m):
            sys.stdout.write(json.dumps(m) + "\n")
            sys.stdout.flush()
        sys.stdout.write(json.dumps({"type":"status","state":"idle"}) + "\n" + SENTINEL + "\n")
        sys.stdout.flush()
        for line in sys.stdin:
            line = line.strip()
            if not line or line == "__INTERRUPT__": continue
            try: msg = json.loads(line)
            except: continue
            if msg.get("type") == "quit": break
            if msg.get("type") != "execute": continue
            code, ec = msg.get("code",""), msg.get("exec_count",0)
            if not code.strip():
                send({"type":"execute_reply","exec_count":ec,"success":True})
                sys.stdout.write(SENTINEL + "\n"); sys.stdout.flush(); continue
            send({"type":"status","state":"busy"})
            old_out, old_err = sys.stdout, sys.stderr
            co, ce = io.StringIO(), io.StringIO()
            ok = True
            try:
                sys.stdout, sys.stderr = co, ce
                try: r = eval(compile(code,"<cell>","eval"),_ns)
                except SyntaxError: exec(compile(code,"<cell>","exec"),_ns); r = None
            except Exception as e:
                ok = False; tb = traceback.format_exception(type(e),e,e.__traceback__)
                sys.stdout, sys.stderr = old_out, old_err
                send({"type":"error","ename":type(e).__name__,"evalue":str(e),"traceback":tb})
            else:
                sys.stdout, sys.stderr = old_out, old_err
            finally:
                sys.stdout, sys.stderr = old_out, old_err
            o = co.getvalue()
            if o: send({"type":"stream","name":"stdout","text":o})
            e2 = ce.getvalue()
            if e2: send({"type":"stream","name":"stderr","text":e2})
            if ok and r is not None: send({"type":"execute_result","data":{"text/plain":repr(r)},"exec_count":ec})
            send({"type":"execute_reply","exec_count":ec,"success":ok})
            send({"type":"status","state":"idle"})
            sys.stdout.write(SENTINEL + "\n"); sys.stdout.flush()
        """;

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _executionCts?.Cancel();
        await ShutdownAsync();
        _executionGate.Dispose();
        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _executionCts?.Cancel();
        KillProcess();
        _executionGate.Dispose();
        GC.SuppressFinalize(this);
    }
}
