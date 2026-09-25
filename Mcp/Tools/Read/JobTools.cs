using CanfarDesktop.Mcp.Tools.Proposals;

namespace CanfarDesktop.Mcp.Tools.Read;

/// <summary>One background job as an agent sees it.</summary>
public sealed record JobView(
    string Id, string Kind, string Summary, string Status, string StartedAt, string? FinishedAt, string? Message)
{
    public static JobView From(BackgroundJob job) => new(
        job.Id, job.Kind, job.Summary,
        job.Status.ToString().ToLowerInvariant(),
        job.StartedAt, job.FinishedAt, job.Message);
}

/// <summary>
/// <c>get_job_status</c> — how a background apply is getting on.
///
/// Some applies take longer than a JSON-RPC request should be held open. Without this, a large download
/// started, the client gave up at its own timeout, and the work vanished with no id, no progress and no
/// error — the caller could not even tell whether it was still running.
/// </summary>
public sealed class GetJobStatusTool : JsonReadTool<GetJobStatusTool.Args, GetJobStatusTool.Output>
{
    private readonly Func<string?, IReadOnlyList<BackgroundJob>> _jobs;

    /// <param name="jobs">All jobs when given null, or the one with that id (empty when unknown).</param>
    public GetJobStatusTool(Func<string?, IReadOnlyList<BackgroundJob>> jobs) => _jobs = jobs;

    public override ToolDescriptor Descriptor { get; } = ToolDescriptor.WithStaticSchema(
        "get_job_status",
        "Check on work started in the background — a long upload, download or apply that answered with " +
        "a job id rather than waiting. Pass `jobId` for one, or nothing for all of them, newest first. " +
        "The id IS the proposal id you were given when the work started. A finished job carries the " +
        "applier's own words: its result, or the reason it failed.",
        """
        {"type":"object","properties":{
          "jobId":{"type":"string","description":"The proposal id the work was started under. Omit for all jobs."}
        },"additionalProperties":false}
        """);

    protected override Task<Output> HandleAsync(Args args, McpToolContext context, CancellationToken ct)
    {
        var id = string.IsNullOrWhiteSpace(args.JobId) ? null : args.JobId!.Trim();
        var jobs = _jobs(id);

        if (id is not null && jobs.Count == 0)
            throw new McpToolException(new UnknownTarget(
                $"no job '{id}' — call get_job_status with no arguments to see the ones there are"));

        var views = jobs.Select(JobView.From).ToList();
        return Task.FromResult(new Output(
            views.Count,
            jobs.Count(j => !j.IsFinished),
            views,
            views.Count == 0 ? "nothing has been started in the background" : null));
    }

    public sealed record Args { public string? JobId { get; init; } }

    public sealed record Output(int Count, int Running, IReadOnlyList<JobView> Jobs, string? Message);
}
