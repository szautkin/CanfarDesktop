namespace CanfarDesktop.Services.Notebook;

using System.Diagnostics;
using System.Text;
using System.Text.Json;

/// <summary>
/// Manages a local Python subprocess for code execution.
/// Communicates via JSON over stdin/stdout with a sentinel delimiter.
/// </summary>
public class LocalKernelService : IKernelService, IAsyncDisposable
{
    private const string Sentinel = "\x04__CANFAR_EXEC_BOUNDARY__\x04";

    private readonly IPythonDiscoveryService _pythonDiscovery;
    private Process? _process;
    private StreamWriter? _stdin;
    private int _executionCount;
    private bool _disposed;
    private string? _harnessPath;
    private readonly object _lock = new();
    private readonly SemaphoreSlim _executionGate = new(1, 1);

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
        if (pythonPath is null)
        {
            SetState(KernelState.Dead);
            throw new InvalidOperationException(
                "Python 3.8+ not found. Install Python from python.org or add it to PATH.");
        }

        // Write harness to temp file
        _harnessPath = Path.Combine(Path.GetTempPath(), $"canfar_kernel_harness_{Environment.ProcessId}.py");
        await WriteHarnessAsync(_harnessPath);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = $"-u \"{_harnessPath}\"", // -u = unbuffered stdout/stderr
                WorkingDirectory = workingDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            // Force UTF-8 on all Python I/O — avoids encoding mismatch with .NET's stdin
            psi.Environment["PYTHONIOENCODING"] = "utf-8";
            psi.Environment["PYTHONUNBUFFERED"] = "1";

            _process = Process.Start(psi);
            if (_process is null || _process.HasExited)
            {
                SetState(KernelState.Dead);
                throw new InvalidOperationException("Failed to start Python process.");
            }

            // Use the process's own StandardInput — do NOT create a second StreamWriter
            // on the same BaseStream (causes buffer corruption / lost bytes)
            _stdin = _process.StandardInput;
            _stdin.AutoFlush = true;
            _executionCount = 0;

            _process.EnableRaisingEvents = true;
            _process.Exited += OnProcessExited;

            // Read the initial "idle" status + boundary
            await ReadUntilBoundaryAsync();

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
        if (State == KernelState.Dead)
            throw new InvalidOperationException("Kernel is not running.");

        await _executionGate.WaitAsync(ct);
        try
        {
            _executionCount++;
            var count = _executionCount;

            SetState(KernelState.Busy);

            // Normalize line endings for Python
            code = code.Replace("\r\n", "\n").Replace("\r", "\n");

            Debug.WriteLine($"[Kernel] Execute ({code.Length} chars): {code[..Math.Min(80, code.Length)]}...");

            var request = JsonSerializer.Serialize(new
            {
                type = "execute",
                code,
                exec_count = count
            });

            await _stdin!.WriteLineAsync(request);

            var outputs = await ReadUntilBoundaryAsync(ct);

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

                    case "status":
                        // handled by boundary logic
                        break;
                }
            }

            // Errors are normal execution results — kernel stays alive and idle
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
            _executionGate.Release();
        }
    }

    public async Task InterruptAsync()
    {
        if (_process is null || _process.HasExited) return;

        try
        {
            // Send interrupt signal via stdin
            await _stdin!.WriteLineAsync("__INTERRUPT__");

            // Wait briefly, then force kill if still busy
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                while (State == KernelState.Busy && !cts.Token.IsCancellationRequested)
                    await Task.Delay(100, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Timeout — kill and restart
                _process.Kill(entireProcessTree: true);
                SetState(KernelState.Dead);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Interrupt failed: {ex.Message}");
        }
    }

    public async Task RestartAsync(string? workingDirectory = null)
    {
        await ShutdownAsync();
        _executionCount = 0;
        await StartAsync(workingDirectory);
    }

    public async Task ShutdownAsync()
    {
        if (_process is null) return;

        try
        {
            if (!_process.HasExited && _stdin is not null)
            {
                await _stdin.WriteLineAsync(JsonSerializer.Serialize(new { type = "quit" }));

                // Wait up to 3 seconds for clean exit
                var exited = _process.WaitForExit(3000);
                if (!exited)
                    _process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Shutdown error: {ex.Message}");
            try { _process.Kill(entireProcessTree: true); } catch { }
        }
        finally
        {
            _process.Exited -= OnProcessExited;
            _process.Dispose();
            _process = null;
            _stdin = null;
            SetState(KernelState.Dead);
            CleanupHarness();
        }
    }

    private async Task<List<JsonElement>> ReadUntilBoundaryAsync(CancellationToken ct = default)
    {
        var messages = new List<JsonElement>();
        if (_process is null) return messages;

        var reader = _process.StandardOutput;
        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null)
            {
                // Process exited mid-read
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
                // Non-JSON output — likely raw print from Python. Treat as stdout stream.
                messages.Add(JsonDocument.Parse(
                    JsonSerializer.Serialize(new { type = "stream", name = "stdout", text = line + "\n" })
                ).RootElement.Clone());
            }
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
            catch { /* best effort */ }
        }
    }

    private static async Task WriteHarnessAsync(string path)
    {
        // Load harness from embedded resource or write inline
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
            // Fallback: write minimal harness inline
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
        await ShutdownAsync();
        _executionGate.Dispose();
        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Synchronous fallback: force-kill, no graceful shutdown (avoids deadlock)
        try { _process?.Kill(entireProcessTree: true); } catch { }
        _process?.Dispose();
        _process = null;
        _stdin = null;
        CleanupHarness();
        _executionGate.Dispose();
        GC.SuppressFinalize(this);
    }
}
