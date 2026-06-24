using System.Text;
using CanfarDesktop.Mcp.Transport;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Mcp.Bridge;

/// <summary>
/// Pure byte-relay logic for the console bridge exe: pump complete documents both ways between two
/// transports (stdio ↔ named pipe), and — when the app isn't running — drain stdin and answer every
/// request with a well-formed serviceUnavailable carrying the matching id. Operates only on
/// <see cref="IMcpTransport"/> so it's testable with InMemoryTransport. 1-to-1 with the macOS bridge.
/// </summary>
public static class BridgeRelay
{
    /// <summary>Relay both directions until either side closes.</summary>
    public static async Task RelayAsync(IMcpTransport stdio, IMcpTransport pipe, CancellationToken cancellationToken = default)
    {
        var toPipe = PumpAsync(stdio, pipe, cancellationToken);
        var toStdio = PumpAsync(pipe, stdio, cancellationToken);
        await Task.WhenAny(toPipe, toStdio);
    }

    private static async Task PumpAsync(IMcpTransport from, IMcpTransport to, CancellationToken ct)
    {
        try
        {
            await foreach (var doc in from.Incoming.ReadAllAsync(ct))
                await to.SendAsync(doc, ct);
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
        finally
        {
            await to.CloseAsync();
        }
    }

    /// <summary>App-not-running fallback: reply serviceUnavailable to each request (notifications ignored).</summary>
    public static async Task DrainAndFailAsync(IMcpTransport stdio, CancellationToken cancellationToken = default)
    {
        await foreach (var doc in stdio.Incoming.ReadAllAsync(cancellationToken))
        {
            var response = ServiceUnavailableFor(doc);
            if (response is not null)
                await stdio.SendAsync(response, cancellationToken);
        }
    }

    /// <summary>Build a serviceUnavailable response for a request document, or null for a notification/garbage.</summary>
    public static byte[]? ServiceUnavailableFor(byte[] requestBytes)
    {
        try
        {
            var request = JsonRpcRequest.Parse(JsonValue.Parse((ReadOnlySpan<byte>)requestBytes));
            if (request.IsNotification) return null;
            var response = JsonRpcResponse.Failure(request.Id,
                new JsonRpcErrorPayload(JsonRpcErrorCode.ServiceUnavailable,
                    "Verbinal is not running (or MCP is disabled). Start the app, enable MCP, then retry."));
            return Encoding.UTF8.GetBytes(response.ToJsonString());
        }
        catch
        {
            return null;
        }
    }
}
