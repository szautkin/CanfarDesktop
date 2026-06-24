using System.Text.Json;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Mcp.Tools;

/// <summary>
/// Base class for read-only tools: fixes VerbClass=Read / AgentSafe=true, deserializes the typed
/// args, applies a timeout, runs the handler, and maps the output (or a thrown
/// <see cref="McpToolException"/> / timeout / unexpected error) to a <see cref="ToolResult"/>.
/// Mirrors the macOS JSONReadTool. <typeparamref name="TArgs"/> must be default-constructible (an
/// absent/empty arguments object yields a default instance).
/// </summary>
public abstract class JsonReadTool<TArgs, TOutput> : IMcpTool where TArgs : new()
{
    public McpVerbClass VerbClass => McpVerbClass.Read;
    public bool AgentSafe => true;
    public abstract ToolDescriptor Descriptor { get; }

    protected virtual TimeSpan Timeout => TimeSpan.FromSeconds(60);

    protected abstract Task<TOutput> HandleAsync(TArgs args, McpToolContext context, CancellationToken cancellationToken);

    public async Task<ToolResult> InvokeAsync(JsonValue arguments, McpToolContext context, CancellationToken cancellationToken)
    {
        TArgs args;
        try
        {
            args = DeserializeArgs(arguments);
        }
        catch (JsonException ex)
        {
            return ToolResult.Fail(new InvalidArgument(ex.Message));
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(Timeout);
            var output = await HandleAsync(args, context, cts.Token);
            return ToolResult.Ok(JsonSerializer.SerializeToUtf8Bytes(output, McpJson.Options));
        }
        catch (McpToolException ex)
        {
            return ToolResult.Fail(ex.Reason);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ToolResult.Fail(new UpstreamTimeout());
        }
        catch (Exception ex)
        {
            return ToolResult.Fail(new BackendError(ex.Message));
        }
    }

    private static TArgs DeserializeArgs(JsonValue arguments)
    {
        if (arguments is JsonNull) return new TArgs();
        return JsonSerializer.Deserialize<TArgs>(arguments.ToJsonString(), McpJson.Options) ?? new TArgs();
    }
}
