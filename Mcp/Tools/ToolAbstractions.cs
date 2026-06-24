using System.Text.Json;
using System.Text.Json.Serialization;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Mcp.Tools;

/// <summary>What a tool does in the gate matrix (the read surface is all <see cref="Read"/>).</summary>
public enum McpVerbClass { Read, SemanticWrite, Destructive, ViewState, ProposalLifecycle, Undo }

/// <summary>Who initiated a tool call — the user (UI) or an external MCP client.</summary>
public abstract record OperationOrigin
{
    public static readonly OperationOrigin User = new UserOrigin();
    public static OperationOrigin External(string clientId) => new ExternalOrigin(clientId);

    public bool IsExternal => this is ExternalOrigin;
    public string Label => this switch { ExternalOrigin e => e.ClientId, _ => "user" };
}

public sealed record UserOrigin : OperationOrigin;
public sealed record ExternalOrigin(string ClientId) : OperationOrigin;

/// <summary>Capabilities + provenance injected into every tool invocation.</summary>
public sealed record McpToolContext(OperationOrigin Origin, Guid RequestId)
{
    public static McpToolContext ForExternal(string clientId, Guid requestId) => new(OperationOrigin.External(clientId), requestId);
    public static McpToolContext ForUser(Guid requestId) => new(OperationOrigin.User, requestId);
}

/// <summary>A tool's manifest entry (<c>tools/list</c>). The schema is parsed once at construction.</summary>
public sealed record ToolDescriptor(string Name, string Description, JsonValue InputSchema)
{
    /// <summary>Build from a literal JSON-Schema string; throws at composition time on a typo.</summary>
    public static ToolDescriptor WithStaticSchema(string name, string description, string schemaJson)
    {
        JsonValue schema;
        try { schema = JsonValue.Parse(schemaJson); }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Tool '{name}' has an invalid input schema: {ex.Message}", ex);
        }
        return new ToolDescriptor(name, description, schema);
    }

    public ToolDefinitionWire ToWire() => new(Name, Description, InputSchema);
}

/// <summary>One tool exposed over MCP.</summary>
public interface IMcpTool
{
    McpVerbClass VerbClass { get; }

    /// <summary>Whether external (MCP-client) callers may invoke this tool. User-only tools set false.</summary>
    bool AgentSafe { get; }

    ToolDescriptor Descriptor { get; }

    Task<ToolResult> InvokeAsync(JsonValue arguments, McpToolContext context, CancellationToken cancellationToken);
}

/// <summary>Shared JSON options for tool argument/output (camelCase, lenient read, omit nulls).</summary>
public static class McpJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

/// <summary>Marker for tools that take no arguments.</summary>
public sealed record EmptyArgs;
