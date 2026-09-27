using System.Net;
using System.Text;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models;
using CanfarDesktop.Models.AICompute;

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
    private readonly IComputeRunStore _runs;

    /// <summary>How often a sent run is checked for its result.</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How long past a run's own timeout to keep waiting before calling it lost. A cold contributed
    /// session takes a minute or two to start, and the watcher only picks the request up once it has.
    /// </summary>
    private static readonly TimeSpan StartupAllowance = TimeSpan.FromMinutes(5);

    public AIComputeService(AIComputeSettingsService settings, ISessionService sessions, IStorageService storage,
        IAuthService auth, IComputeRunStore runs)
    {
        _settings = settings;
        _sessions = sessions;
        _storage = storage;
        _auth = auth;
        _runs = runs;
    }

    /// <summary>Every run sent from this app, by an agent or by the person, newest first.</summary>
    public IComputeRunStore Runs => _runs;

    /// <summary>Whether somebody is signed in to CANFAR — the session and the exec folder are theirs.</summary>
    public bool IsSignedIn => _auth.CurrentUsername is { Length: > 0 };

    /// <summary>Whether a compute image is set — without one, nothing here can run.</summary>
    public bool IsConfigured => _settings.Settings.IsEnabled;

    /// <summary>The (cores, RAM) the session is launched with.</summary>
    public (int Cores, int Ram) Size => _settings.ResolveResources();

    /// <summary>The image the session is launched from, as configured and resolved.</summary>
    public string Image => _settings.ResolveImage();

    /// <summary>
    /// The compute session as the platform has it, whatever its state — null when there is none.
    ///
    /// <para>Unlike the warm-session lookup that decides whether to launch, this also returns a failed
    /// or terminating session: the Remote Compute screen has to say so, not claim there is nothing.
    /// A live one wins over a dead one of the same name.</para>
    /// </summary>
    public async Task<Session?> CurrentSessionAsync(CancellationToken ct = default)
    {
        var ours = (await _sessions.GetSessionsAsync(ct)).Where(IsComputeSession).ToList();
        return ours.FirstOrDefault(s => IsLive(s.Status)) ?? ours.FirstOrDefault();
    }

    /// <summary>
    /// Where remote compute stands — the one answer the Remote Compute screen and get_compute_state
    /// both give. The session is looked for whether or not an image is set here: one left from another
    /// install is still the person's, and still holding their cores. Signed out, there is no session
    /// to look for.
    /// </summary>
    public async Task<ComputeSnapshot> SnapshotAsync(CancellationToken ct = default)
    {
        var (cores, ram) = Size;
        var configured = IsConfigured;
        var session = IsSignedIn ? await CurrentSessionAsync(ct) : null;
        return new(ComputeStatus.From(configured, session?.Status), session,
            configured ? Image : string.Empty, cores, ram, configured);
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

    /// <summary>
    /// Ensure the compute session, then drop the request file in the inbox. Returns without waiting for
    /// a result — the caller polls <see cref="FetchOutAsync"/> (run_code_output).
    ///
    /// <para>Every run is remembered with who sent it, shown in the activity bar while it is out, and
    /// watched here until its result arrives or it is given up on — so the Remote Compute screen and
    /// the activity bar settle whether or not anybody polls for the output.</para>
    /// </summary>
    public async Task SubmitAsync(RunCodeRequest request, ComputeRunAuthor author, CancellationToken ct = default)
    {
        var user = RequireUsername();

        // Whoever wrote it — the Run code box, Run again, an agent's run_code — it leaves with Unix line
        // endings, and is remembered the way it was sent.
        request = request with { Code = RunCodeContract.NormalizeNewlines(request.Code) };
        _runs.Add(new ComputeRun(request.Id, author, request.Language, request.Code, request.TimeoutSeconds, IsoTime.Now()));

        var who = author == ComputeRunAuthor.Agent ? "Assistant" : "You";
        var task = TaskRegistry.Begin(TaskKind.Session, $"{who}: {request.Language} on {RunCodeContract.SessionName}");
        try
        {
            await EnsureSessionAsync(ct);
            await EnsureInboxTreeAsync(user, ct);

            var json = RunCodeJson.SerializeRequest(request);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
            await _storage.UploadFileAsync(RunCodeContract.InboxPath(user, request.Id), stream, "application/json", ct);
        }
        catch (Exception ex)
        {
            _runs.Close(request.Id, ComputeRun.NotSent);
            task.Fail(ex.Message);
            throw;
        }

        task.Stage("Waiting for the result");
        _ = WatchAsync(request, task);
    }

    /// <summary>Poll for a run's result until it arrives or the run cannot still be going.</summary>
    private async Task WatchAsync(RunCodeRequest request, TaskHandle task)
    {
        var giveUpAt = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(request.TimeoutSeconds) + StartupAllowance;
        try
        {
            while (DateTimeOffset.UtcNow < giveUpAt)
            {
                // Somebody else — run_code_output, or the screen — may have read it already.
                if (_runs.Find(request.Id) is { IsFinished: true } done)
                {
                    Settle(task, done.Status);
                    return;
                }

                if (await FetchOutAsync(request.Id) is { } result)
                {
                    Settle(task, result.Status);
                    return;
                }

                await Task.Delay(PollInterval);
            }

            _runs.Close(request.Id, ComputeRun.NoResult);
            task.Fail("No result came back");
        }
        catch (Exception ex)
        {
            task.Fail(ex.Message);
        }
    }

    private static void Settle(TaskHandle task, string? status)
    {
        if (string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase)) task.Succeed();
        else task.Fail(status ?? "error");
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
            var result = RunCodeJson.TryParseResult(text);

            // Whoever reads the result first records it, so the history does not wait on the watcher loop.
            if (result is not null) _runs.Complete(id, result);
            return result;
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
        return sessions.FirstOrDefault(s => IsComputeSession(s) && IsLive(s.Status));
    }

    /// <summary>
    /// Whether a session is the compute session. Matched by name and type, not image, so it survives
    /// registry-prefix normalisation — and so the Portal can mark it as the assistant's.
    /// </summary>
    public static bool IsComputeSession(Session s)
        => string.Equals(s.SessionType, RunCodeContract.SessionType, StringComparison.OrdinalIgnoreCase)
           && string.Equals(s.SessionName, RunCodeContract.SessionName, StringComparison.Ordinal);

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
