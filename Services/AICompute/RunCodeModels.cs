using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CanfarDesktop.Services.AICompute;

/// <summary>The request file dropped in the inbox: <c>{id, language, code, timeout_seconds}</c>.</summary>
public sealed record RunCodeRequest(string Id, string Language, string Code, int TimeoutSeconds);

/// <summary>
/// The result file the watcher writes: <c>{status, exit_code, stdout, stdout_encoding, stderr,
/// stderr_encoding, duration_ms, truncated, started_at, finished_at}</c>. <c>status</c> is authoritative
/// (<c>ok|error|timeout</c>); stdout/stderr are utf8 unless the matching <c>*_encoding</c> is <c>base64</c>.
/// </summary>
public sealed record RunCodeResult(
    string? Status, int? ExitCode,
    string? Stdout, string? StdoutEncoding,
    string? Stderr, string? StderrEncoding,
    long? DurationMs, bool? Truncated, string? StartedAt, string? FinishedAt)
{
    public string? DecodedStdout() => Decode(Stdout, StdoutEncoding);
    public string? DecodedStderr() => Decode(Stderr, StderrEncoding);

    private static string? Decode(string? value, string? encoding)
    {
        if (value is null) return null;
        if (string.Equals(encoding, "base64", StringComparison.OrdinalIgnoreCase))
        {
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(value)); }
            catch (FormatException) { return value; }
        }
        return value;
    }
}

/// <summary>Pure (de)serialization of the run_code wire files — snake_case to match the watcher contract.</summary>
public static class RunCodeJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string SerializeRequest(RunCodeRequest request) => JsonSerializer.Serialize(request, Options);

    /// <summary>Lenient parse of the result file; null when absent/blank/incomplete (read-after-write lag).</summary>
    public static RunCodeResult? TryParseResult(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<RunCodeResult>(json, Options); }
        catch (JsonException) { return null; }
    }
}
