using System.Net;
using System.Text;
using CanfarDesktop.Models;

namespace CanfarDesktop.Services.AICompute;

/// <summary>
/// Runs agent-authored code on remote compute via the file-drop RPC the external <c>verbinal-execution</c>
/// watcher consumes: reuse (or lazily launch, without waiting for Running) one <c>contributed</c> session
/// named <see cref="RunCodeContract.SessionName"/>, PUT the request to the shared /arc inbox, and poll the
/// out file. Reuses the existing session + VOSpace services; no new HTTP plumbing. The watcher image
/// itself is external — until it is built + set as the compute image, submitted code simply never
/// produces an out file (the output poll stays "not ready").
/// </summary>
public sealed class AIComputeService
{
    private readonly AIComputeSettingsService _settings;
    private readonly ISessionService _sessions;
    private readonly IStorageService _storage;
    private readonly IAuthService _auth;

    public AIComputeService(AIComputeSettingsService settings, ISessionService sessions, IStorageService storage, IAuthService auth)
    {
        _settings = settings;
        _sessions = sessions;
        _storage = storage;
        _auth = auth;
    }

    /// <summary>Reuse the warm verbinal-compute session, or launch one at the configured size. Does NOT
    /// wait for Running (a contributed launch routinely takes 60–90s; the watcher re-scans the inbox on
    /// boot). Throws when no compute image is configured.</summary>
    public async Task EnsureSessionAsync(CancellationToken ct = default)
    {
        var image = _settings.ResolveImage();
        if (string.IsNullOrEmpty(image))
            throw new InvalidOperationException("No AI compute image configured. Set one in Settings ▸ AI compute.");

        if (await FindWarmSessionAsync(ct) is not null) return;

        var (cores, ram) = _settings.ResolveResources();
        var (regUser, regSecret) = _settings.RegistryCredentials();
        await _sessions.LaunchSessionAsync(new SessionLaunchParams
        {
            Type = RunCodeContract.SessionType,
            Name = RunCodeContract.SessionName,
            Image = image,
            Cores = cores,
            Ram = ram,
            Gpus = 0,
            // The interactive launch path honours RegistryUsername/Secret (not the pre-built header).
            RegistryUsername = string.IsNullOrEmpty(regUser) ? null : regUser,
            RegistrySecret = string.IsNullOrEmpty(regSecret) ? null : regSecret,
        }, ct);
    }

    /// <summary>Ensure the compute session, then drop the request file in the inbox. Returns without
    /// waiting for a result — the caller polls <see cref="FetchOutAsync"/> (run_code_output).</summary>
    public async Task SubmitAsync(RunCodeRequest request, CancellationToken ct = default)
    {
        var user = RequireUsername();
        await EnsureSessionAsync(ct);
        await EnsureInboxTreeAsync(user, ct);

        var json = RunCodeJson.SerializeRequest(request);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        await _storage.UploadFileAsync(RunCodeContract.InboxPath(user, request.Id), stream, "application/json", ct);
    }

    /// <summary>Read + parse the result file for an execution id; null when it isn't ready yet (absent,
    /// 404, or mid-write).</summary>
    public async Task<RunCodeResult?> FetchOutAsync(string id, CancellationToken ct = default)
    {
        var user = RequireUsername();
        try
        {
            await using var stream = await _storage.DownloadFileAsync(RunCodeContract.OutPath(user, id), ct);
            var text = await ReadBoundedAsync(stream, RunCodeContract.MaxResultBytes, ct);
            return RunCodeJson.TryParseResult(text);
        }
        catch (HttpRequestException)
        {
            return null; // 404 = not ready yet (or transient) — the caller polls again
        }
    }

    /// <summary>Stop the warm compute session (idempotent — no-op when none is running).</summary>
    public async Task<bool> StopAsync(CancellationToken ct = default)
    {
        var session = await FindWarmSessionAsync(ct);
        if (session is null) return false;
        return await _sessions.DeleteSessionAsync(session.Id, ct);
    }

    private async Task<Session?> FindWarmSessionAsync(CancellationToken ct)
    {
        var sessions = await _sessions.GetSessionsAsync(ct);
        // Reuse by NAME (not image — survives registry-prefix normalization); count Pending so rapid
        // cold-start calls don't spawn duplicates.
        return sessions.FirstOrDefault(s =>
            string.Equals(s.SessionType, RunCodeContract.SessionType, StringComparison.OrdinalIgnoreCase)
            && string.Equals(s.SessionName, RunCodeContract.SessionName, StringComparison.Ordinal)
            && IsLive(s.Status));
    }

    private static bool IsLive(string status) =>
        status.Equals("Running", StringComparison.OrdinalIgnoreCase)
        || status.Equals("Pending", StringComparison.OrdinalIgnoreCase);

    /// <summary>Create the inbox folder tree one level at a time (CreateFolderAsync rejects '/'),
    /// tolerating an already-exists 409.</summary>
    private async Task EnsureInboxTreeAsync(string user, CancellationToken ct)
    {
        foreach (var level in RunCodeContract.InboxTreeLevels)
        {
            var slash = level.LastIndexOf('/');
            var parent = slash < 0 ? user : $"{user}/{level[..slash]}";
            var folder = slash < 0 ? level : level[(slash + 1)..];
            try
            {
                await _storage.CreateFolderAsync(parent, folder, ct);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                // Already exists — fine.
            }
        }
    }

    private static async Task<string> ReadBoundedAsync(Stream stream, int maxBytes, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while (ms.Length < maxBytes && (read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            var take = (int)Math.Min(read, maxBytes - ms.Length);
            ms.Write(buffer, 0, take);
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private string RequireUsername() => _auth.CurrentUsername is { Length: > 0 } u
        ? u
        : throw new InvalidOperationException("Sign in to CANFAR before using run_code.");
}
