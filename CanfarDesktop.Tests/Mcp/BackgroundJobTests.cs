using System.Text.Json;
using Xunit;
using CanfarDesktop.Mcp.Tools;
using CanfarDesktop.Mcp.Tools.Proposals;
using CanfarDesktop.Mcp.Tools.Read;
using CanfarDesktop.Mcp.Tools.Write;
using CanfarDesktop.Mcp.Wire;

namespace CanfarDesktop.Tests.Mcp;

/// <summary>
/// Work that outlives the call that asked for it.
///
/// The failure this removes: a large download applied inline, the client gave up at its own timeout,
/// and the work vanished — no id, no progress, no error, and no way to tell whether it was still
/// running.
/// </summary>
public class BackgroundJobTests
{
    private static JsonValue Args(string json) => JsonValue.Parse(json);
    private static McpToolContext Ctx() => McpToolContext.ForExternal("c1", Guid.NewGuid());

    private static T Payload<T>(ToolResult result)
        => JsonSerializer.Deserialize<T>(Assert.IsType<DataResult>(result).Json, McpJson.Options)!;

    private static string Failure(ToolResult result) => Assert.IsType<FailedResult>(result).Reason.Description;

    /// <summary>An applier that finishes when told to, so a job can be observed mid-flight.</summary>
    private sealed class GatedApplier : IProposalApplier
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public GatedApplier(string kind) => Kind = kind;

        public string Kind { get; }

        public Task ApplyAsync(PendingProposal proposal, CancellationToken cancellationToken = default) => _gate.Task;

        public void Succeed() => _gate.TrySetResult();
        public void Fail(string why) => _gate.TrySetException(new InvalidOperationException(why));
    }

    private static PendingProposal Queue(IProposalStore store, string kind = "download_observation")
        => store.Enqueue(PendingProposal.Create(kind, kind, $"Download something",
            System.Text.Encoding.UTF8.GetBytes("{}"), OperationOrigin.External("c1"), Guid.NewGuid()));

    // ── The registry ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AStartedJobIsRunningUntilItIsFinished()
    {
        var jobs = new JobRegistry();
        jobs.Start("a", "download_observation", "Download M31");

        Assert.Equal(JobStatus.Running, jobs.Get("a")!.Status);
        Assert.Equal(1, jobs.RunningCount());

        jobs.Finish("a", succeeded: true, message: "saved");

        Assert.Equal(JobStatus.Succeeded, jobs.Get("a")!.Status);
        Assert.Equal("saved", jobs.Get("a")!.Message);
        Assert.NotNull(jobs.Get("a")!.FinishedAt);
        Assert.Equal(0, jobs.RunningCount());
    }

    [Fact]
    public void AFailedJobKeepsTheReason()
    {
        var jobs = new JobRegistry();
        jobs.Start("a", "k", "s");
        jobs.Finish("a", succeeded: false, message: "the service refused it");

        Assert.Equal(JobStatus.Failed, jobs.Get("a")!.Status);
        Assert.Equal("the service refused it", jobs.Get("a")!.Message);
    }

    /// <summary>Two rows with one id is a list a caller cannot read; the newer attempt is the one asked about.</summary>
    [Fact]
    public void RestartingAnIdReplacesItsRecord()
    {
        var jobs = new JobRegistry();
        jobs.Start("a", "k", "first");
        jobs.Finish("a", succeeded: false, message: "failed");
        jobs.Start("a", "k", "second");

        Assert.Single(jobs.All());
        Assert.Equal(JobStatus.Running, jobs.Get("a")!.Status);
        Assert.Equal("second", jobs.Get("a")!.Summary);
    }

    /// <summary>
    /// A running job is never evicted whatever the cap says: dropping it would leave work in flight that
    /// nothing can report on, which is the exact problem this class exists to prevent.
    /// </summary>
    [Fact]
    public void ARunningJobSurvivesEvictionThatDropsFinishedOnes()
    {
        var jobs = new JobRegistry();
        jobs.Start("keep-me", "k", "still going");

        for (var i = 0; i < 80; i++)
        {
            jobs.Start($"done-{i}", "k", "over");
            jobs.Finish($"done-{i}", succeeded: true, message: null);
        }

        Assert.NotNull(jobs.Get("keep-me"));
        Assert.Equal(JobStatus.Running, jobs.Get("keep-me")!.Status);
        Assert.True(jobs.All().Count <= 51);   // the cap on finished ones, plus the running one
    }

    [Fact]
    public void FinishingSomethingUnknownIsIgnoredRatherThanInvented()
    {
        var jobs = new JobRegistry();
        jobs.Finish("never-started", succeeded: true, message: null);

        Assert.Empty(jobs.All());
    }

    [Fact]
    public void JobsAreListedNewestFirst()
    {
        var jobs = new JobRegistry();
        jobs.Start("first", "k", "1");
        jobs.Start("second", "k", "2");

        Assert.Equal(["second", "first"], jobs.All().Select(j => j.Id));
    }

    // ── start_background_apply ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StartingAnApplyAnswersAtOnceWithTheProposalsOwnId()
    {
        var store = new InMemoryProposalStore();
        var appliers = new ProposalApplierRegistry();
        var applier = new GatedApplier("download_observation");
        appliers.Register(applier);
        var jobs = new JobRegistry();

        var proposal = Queue(store);
        var runner = new BackgroundApplyRunner(store, appliers, jobs);

        var outcome = await runner.StartAsync(proposal.Id.ToString());

        // Answered while the apply is still gated: not waiting is the entire point.
        Assert.True(outcome.Started);
        Assert.Equal(proposal.Id.ToString(), outcome.JobId);
        Assert.Equal("running", outcome.Status);
        Assert.Equal(JobStatus.Running, jobs.Get(proposal.Id.ToString())!.Status);

        applier.Succeed();
    }

    [Fact]
    public async Task AFinishedApplyIsRecordedAsSucceeded()
    {
        var store = new InMemoryProposalStore();
        var appliers = new ProposalApplierRegistry();
        var applier = new GatedApplier("download_observation");
        appliers.Register(applier);
        var jobs = new JobRegistry();

        var proposal = Queue(store);
        await new BackgroundApplyRunner(store, appliers, jobs).StartAsync(proposal.Id.ToString());

        applier.Succeed();
        await WaitForFinish(jobs, proposal.Id.ToString());

        Assert.Equal(JobStatus.Succeeded, jobs.Get(proposal.Id.ToString())!.Status);
        Assert.Equal(ProposalState.Applied, store.State(proposal.Id));
    }

    /// <summary>
    /// A failure is REPORTED rather than disappearing into a dropped task — which is the failure mode
    /// this whole mechanism exists to remove.
    /// </summary>
    [Fact]
    public async Task AFailedApplyIsRecordedWithItsReason()
    {
        var store = new InMemoryProposalStore();
        var appliers = new ProposalApplierRegistry();
        var applier = new GatedApplier("download_observation");
        appliers.Register(applier);
        var jobs = new JobRegistry();

        var proposal = Queue(store);
        await new BackgroundApplyRunner(store, appliers, jobs).StartAsync(proposal.Id.ToString());

        applier.Fail("the service refused it");
        await WaitForFinish(jobs, proposal.Id.ToString());

        var job = jobs.Get(proposal.Id.ToString())!;
        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Contains("refused", job.Message);
    }

    [Fact]
    public async Task AProposalThatIsNotThereIsRefusedWithWhereToLook()
    {
        var runner = new BackgroundApplyRunner(new InMemoryProposalStore(), new ProposalApplierRegistry(), new JobRegistry());

        var outcome = await runner.StartAsync(Guid.NewGuid().ToString());

        Assert.False(outcome.Started);
        Assert.Contains("list_pending_proposals", outcome.Message);
    }

    [Fact]
    public async Task SomethingThatIsNotAnIdIsSaidToBeOne()
    {
        var runner = new BackgroundApplyRunner(new InMemoryProposalStore(), new ProposalApplierRegistry(), new JobRegistry());

        var outcome = await runner.StartAsync("the-big-one");

        Assert.False(outcome.Started);
        Assert.Contains("not a proposal id", outcome.Message);
    }

    /// <summary>A kind with no applier cannot be applied at all — saying so beats a job that never moves.</summary>
    [Fact]
    public async Task AKindWithNoApplierIsRefusedRatherThanStarted()
    {
        var store = new InMemoryProposalStore();
        var jobs = new JobRegistry();
        var proposal = Queue(store, "some_unwired_kind");

        var outcome = await new BackgroundApplyRunner(store, new ProposalApplierRegistry(), jobs)
            .StartAsync(proposal.Id.ToString());

        Assert.False(outcome.Started);
        Assert.Contains("no applier", outcome.Message);
        Assert.Empty(jobs.All());
    }

    [Fact]
    public async Task TheToolRefusesAnEmptyId()
    {
        var tool = new StartBackgroundApplyTool(_ => Task.FromResult(BackgroundApplyOutcome.Refused("x")));

        Assert.Contains("proposalId is required",
            Failure(await tool.InvokeAsync(Args("""{"proposalId":"  "}"""), Ctx(), default)));
    }

    /// <summary>The WRITE was the proposal, which already went through the gate. This only declines to wait.</summary>
    [Fact]
    public void StartingInTheBackgroundIsNotItselfAWrite()
        => Assert.Equal(McpVerbClass.ViewState,
            new StartBackgroundApplyTool(_ => Task.FromResult(BackgroundApplyOutcome.Refused("x"))).VerbClass);

    // ── get_job_status ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AskingAboutOneJobAnswersAboutThatJob()
    {
        var jobs = new JobRegistry();
        jobs.Start("a", "download_observation", "Download M31");

        var tool = new GetJobStatusTool(id => id is null ? jobs.All() : jobs.Get(id) is { } j ? [j] : []);
        var output = Payload<GetJobStatusTool.Output>(
            await tool.InvokeAsync(Args("""{"jobId":"a"}"""), Ctx(), default));

        Assert.Equal(1, output.Count);
        Assert.Equal(1, output.Running);
        Assert.Equal("running", output.Jobs[0].Status);
        Assert.Equal("Download M31", output.Jobs[0].Summary);
    }

    [Fact]
    public async Task AskingAboutNothingInParticularAnswersAboutEverything()
    {
        var jobs = new JobRegistry();
        jobs.Start("a", "k", "one");
        jobs.Start("b", "k", "two");
        jobs.Finish("b", succeeded: true, message: null);

        var tool = new GetJobStatusTool(id => id is null ? jobs.All() : jobs.Get(id) is { } j ? [j] : []);
        var output = Payload<GetJobStatusTool.Output>(await tool.InvokeAsync(Args("{}"), Ctx(), default));

        Assert.Equal(2, output.Count);
        Assert.Equal(1, output.Running);
    }

    [Fact]
    public async Task AnUnknownJobIdSaysHowToSeeTheRealOnes()
    {
        var tool = new GetJobStatusTool(_ => []);

        Assert.Contains("get_job_status with no arguments",
            Failure(await tool.InvokeAsync(Args("""{"jobId":"nope"}"""), Ctx(), default)));
    }

    [Fact]
    public async Task WithNothingStartedItSaysSo()
    {
        var tool = new GetJobStatusTool(_ => []);
        var output = Payload<GetJobStatusTool.Output>(await tool.InvokeAsync(Args("{}"), Ctx(), default));

        Assert.Contains("nothing has been started", output.Message);
    }

    private static async Task WaitForFinish(JobRegistry jobs, string id)
    {
        for (var i = 0; i < 100 && jobs.Get(id)?.IsFinished != true; i++)
            await Task.Delay(10);
    }
}
