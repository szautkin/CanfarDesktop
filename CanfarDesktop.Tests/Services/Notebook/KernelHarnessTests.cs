using Xunit;
using Xunit.Abstractions;
using CanfarDesktop.Services.Notebook;

namespace CanfarDesktop.Tests.Services.Notebook;

/// <summary>
/// One kernel for the whole class: starting Python takes seconds, and each test uses names of its own.
/// Null when this machine has no Python — the harness cannot be exercised without one.
/// </summary>
public sealed class KernelFixture : IAsyncLifetime
{
    public LocalKernelService? Kernel { get; private set; }

    public async Task InitializeAsync()
    {
        var python = new PythonDiscoveryService();
        if (await python.FindPythonAsync() is null) return;

        Kernel = new LocalKernelService(python);
        await Kernel.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (Kernel is not null) await Kernel.DisposeAsync();
    }
}

/// <summary>
/// What a notebook cell shows, run through the real kernel harness — the Python script the app
/// embeds and starts, not a stand-in.
///
/// <para>A cell showed the value of its last line only when the WHOLE cell was one expression. The
/// commonest notebook habit — a few statements, then the thing to look at (<c>df.head()</c>) —
/// showed nothing for that last line, where Jupyter shows <c>Out[n]</c>.</para>
/// </summary>
public sealed class KernelHarnessTests : IClassFixture<KernelFixture>
{
    private readonly KernelFixture _fixture;
    private readonly ITestOutputHelper _output;

    public KernelHarnessTests(KernelFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    /// <summary>The cell's outputs, or null — said out loud — when there is no Python here.</summary>
    private async Task<List<KernelOutput>?> RunAsync(string code)
    {
        if (_fixture.Kernel is null)
        {
            _output.WriteLine("No Python on this machine: the kernel harness was not exercised.");
            return null;
        }
        return (await _fixture.Kernel.ExecuteAsync(code)).Outputs;
    }

    private static string? Result(List<KernelOutput> outputs)
        => outputs.SingleOrDefault(o => o.OutputType == "execute_result")?.Text;

    /// <summary>The test has to run the real harness: without the resource the kernel quietly falls back to a minimal one.</summary>
    [Fact]
    public void TheRealHarnessIsWhatTheKernelRuns()
        => Assert.NotNull(typeof(LocalKernelService).Assembly
            .GetManifestResourceStream("CanfarDesktop.Resources.Notebook.kernel_harness.py"));

    [Fact]
    public async Task ACellOfStatementsShowsItsLastExpression()
    {
        if (await RunAsync("stmt_a = 20\nstmt_b = 1\nstmt_a + stmt_b") is not { } outputs) return;

        Assert.Equal("21", Result(outputs));
    }

    [Fact]
    public async Task PrintedOutputComesBeforeTheValue()
    {
        if (await RunAsync("print('first')\n21 * 2") is not { } outputs) return;

        Assert.Equal("stream", outputs[0].OutputType);
        Assert.Equal("first\n", outputs[0].Text);
        Assert.Equal("42", Result(outputs));
    }

    [Fact]
    public async Task ASingleExpressionStillShowsItsValue()
    {
        if (await RunAsync("6 * 7") is not { } outputs) return;

        Assert.Equal("42", Result(outputs));
    }

    [Fact]
    public async Task ACellEndingInAStatementShowsNoValue()
    {
        if (await RunAsync("stmt_c = 5\nstmt_d = stmt_c * 2") is not { } outputs) return;

        Assert.Null(Result(outputs));
    }

    /// <summary>As in Jupyter, a trailing semicolon keeps the value to itself.</summary>
    [Fact]
    public async Task ATrailingSemicolonHidesTheValue()
    {
        if (await RunAsync("stmt_e = 3\nstmt_e + 1;") is not { } outputs) return;

        Assert.Null(Result(outputs));
    }

    /// <summary>A last line that returns None — a print, a plot call — shows no Out.</summary>
    [Fact]
    public async Task ANoneValueIsNotShown()
    {
        if (await RunAsync("stmt_f = 1\nprint(stmt_f)") is not { } outputs) return;

        Assert.Null(Result(outputs));
        Assert.Equal("1\n", Assert.Single(outputs).Text);
    }

    /// <summary>An error in the last line is that error alone, after what ran before it printed.</summary>
    [Fact]
    public async Task AnErrorInTheLastExpressionIsReportedOnce()
    {
        if (await RunAsync("print('before')\n1 / 0") is not { } outputs) return;

        var error = Assert.Single(outputs, o => o.OutputType == "error");
        Assert.Equal("ZeroDivisionError", error.Ename);
        Assert.DoesNotContain(error.Traceback ?? [], line => line.Contains("SyntaxError"));
        Assert.Contains(outputs, o => o.OutputType == "stream" && o.Text == "before\n");
        Assert.Null(Result(outputs));
    }
}
