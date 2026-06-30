using Xunit;
using CanfarDesktop.Models.AICompute;
using CanfarDesktop.Services.AICompute;

namespace CanfarDesktop.Tests.Services;

/// <summary>The pure run_code contract + wire records that must match the external watcher byte-for-byte.</summary>
public class RunCodeContractTests
{
    [Theory]
    [InlineData(0, 60)]      // zero → default
    [InlineData(-5, 60)]     // negative → default
    [InlineData(30, 30)]
    [InlineData(5000, 900)]  // clamp to max
    public void ClampTimeout_BoundsAndDefaults(int input, int expected)
        => Assert.Equal(expected, RunCodeContract.ClampTimeout(input));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(200, 64)]    // clamp cores to 64
    public void ClampCores_BoundsAndDefaults(int input, int expected)
        => Assert.Equal(expected, RunCodeContract.ClampCores(input));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(9999, 256)]  // clamp ram to 256
    public void ClampRam_BoundsAndDefaults(int input, int expected)
        => Assert.Equal(expected, RunCodeContract.ClampRam(input));

    [Theory]
    [InlineData("python", "python")]
    [InlineData("BASH", "bash")]
    [InlineData("  Python ", "python")]
    [InlineData("ruby", "python")]   // unsupported → python
    [InlineData(null, "python")]
    public void NormalizeLanguage_OnlyPythonOrBash(string? input, string expected)
        => Assert.Equal(expected, RunCodeContract.NormalizeLanguage(input));

    [Fact]
    public void SanitizeId_ReplacesUnsafeFilenameChars()
    {
        Assert.Equal("a_b_c_d", RunCodeContract.SanitizeId("a/b:c\\d"));
        Assert.Equal("ok-123", RunCodeContract.SanitizeId("ok-123")); // safe id unchanged
    }

    [Fact]
    public void InboxOutPath_AreUsernameRootedUnderDotVerbinal()
    {
        Assert.Equal("szautkin/.verbinal/exec/inbox/abc.json", RunCodeContract.InboxPath("szautkin", "abc"));
        Assert.Equal("szautkin/.verbinal/exec/out/abc.json", RunCodeContract.OutPath("szautkin", "abc"));
        // an unsafe id is sanitized into the path
        Assert.Equal("u/.verbinal/exec/inbox/a_b.json", RunCodeContract.InboxPath("u", "a/b"));
    }

    // ── wire records ──

    [Fact]
    public void SerializeRequest_IsSnakeCase()
    {
        var json = RunCodeJson.SerializeRequest(new RunCodeRequest("id1", "python", "print(1)", 60));
        Assert.Contains("\"id\":\"id1\"", json);
        Assert.Contains("\"timeout_seconds\":60", json);
        Assert.Contains("\"code\":\"print(1)\"", json);
    }

    [Fact]
    public void TryParseResult_ParsesFullResult()
    {
        var r = RunCodeJson.TryParseResult("""
            {"status":"ok","exit_code":0,"stdout":"hi","stdout_encoding":"utf8","duration_ms":42,"truncated":false}
            """);
        Assert.NotNull(r);
        Assert.Equal("ok", r!.Status);
        Assert.Equal(0, r.ExitCode);
        Assert.Equal("hi", r.DecodedStdout());
        Assert.Equal(42, r.DurationMs);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ not json")]
    public void TryParseResult_BlankOrGarbage_ReturnsNull(string json)
        => Assert.Null(RunCodeJson.TryParseResult(json));

    [Fact]
    public void DecodedStdout_HonorsBase64Encoding()
    {
        var b64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("héllo"));
        var r = new RunCodeResult("ok", 0, b64, "base64", null, null, null, null, null, null);
        Assert.Equal("héllo", r.DecodedStdout());
    }

    // ── settings model ──

    [Fact]
    public void Settings_IsEnabled_OnlyWithImage()
    {
        Assert.False(new AIComputeSettings().IsEnabled);                       // empty image
        Assert.True(new AIComputeSettings { Image = "img:1" }.IsEnabled);
    }

    [Fact]
    public void Settings_IsAllDefaults()
    {
        Assert.True(new AIComputeSettings().IsAllDefaults);
        Assert.False(new AIComputeSettings { Image = "img:1" }.IsAllDefaults);
        Assert.False(new AIComputeSettings { Cores = 4 }.IsAllDefaults);
        Assert.False(new AIComputeSettings { RegistryRepository = "project" }.IsAllDefaults);
    }
}
