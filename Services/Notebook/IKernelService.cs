namespace CanfarDesktop.Services.Notebook;

/// <summary>
/// Abstraction over a code execution backend. Local implementation runs
/// a Python subprocess. Future remote implementation uses WebSockets to
/// a Jupyter kernel gateway on CANFAR.
/// </summary>
public interface IKernelService : IDisposable
{
    KernelState State { get; }
    event Action<KernelState>? StateChanged;
    event Action<KernelOutput>? OutputReceived;

    Task StartAsync(string? workingDirectory = null);
    Task<ExecutionResult> ExecuteAsync(string code, CancellationToken ct = default);
    Task InterruptAsync();
    Task RestartAsync(string? workingDirectory = null);
    Task ShutdownAsync();
}

/// <summary>
/// A single output item produced during execution.
/// </summary>
public class KernelOutput
{
    public required string OutputType { get; init; } // "stream", "execute_result", "display_data", "error"
    public string? StreamName { get; init; }          // "stdout" or "stderr" for stream
    public string? Text { get; init; }                // text content
    public Dictionary<string, string>? Data { get; init; } // mime-type → content for rich output
    public string? Ename { get; init; }
    public string? Evalue { get; init; }
    public List<string>? Traceback { get; init; }
}

/// <summary>
/// Result of a single cell execution.
/// </summary>
public class ExecutionResult
{
    public bool Success { get; init; }
    public int ExecutionCount { get; init; }
    public List<KernelOutput> Outputs { get; init; } = [];
}
