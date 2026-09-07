using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// The two agent cues.
///
/// What is worth testing here is not the noise — it is WHEN it happens and what happens when it can't.
/// The activity signal fires once per tool call, many times a second during real work, so the cue has to
/// land on the edges or it is a stutter; and a machine with no sound must simply be quiet rather than
/// raise anything.
/// </summary>
public class AgentSoundTests
{
    // ── When a cue is due ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheFirstToolCallStartsIt()
    {
        var cues = new AgentCueTracker();

        Assert.Equal(AgentCue.Started, cues.Activity());
        Assert.True(cues.Working);
    }

    /// <summary>The failure this prevents: one sound per tool call, which is a stutter, not a cue.</summary>
    [Fact]
    public void ABurstOfToolCallsSoundsOnce()
    {
        var cues = new AgentCueTracker();
        cues.Activity();

        for (var i = 0; i < 50; i++)
            Assert.Null(cues.Activity());
    }

    [Fact]
    public void GoingQuietFinishesIt()
    {
        var cues = new AgentCueTracker();
        cues.Activity();

        Assert.Equal(AgentCue.Finished, cues.Idle());
        Assert.False(cues.Working);
    }

    /// <summary>An idle timer that ticks with nothing running must not announce a finish that never began.</summary>
    [Fact]
    public void GoingQuietWhenNothingWasRunningSaysNothing()
        => Assert.Null(new AgentCueTracker().Idle());

    [Fact]
    public void ASecondRunStartsAgain()
    {
        var cues = new AgentCueTracker();
        cues.Activity();
        cues.Idle();

        Assert.Equal(AgentCue.Started, cues.Activity());
    }

    // ── The files ───────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(AgentCue.Started, "agent-start.wav")]
    [InlineData(AgentCue.Finished, "agent-stop.wav")]
    public void EachCueHasItsFileInstalledBesideTheBinary(AgentCue cue, string expected)
    {
        var path = AgentSounds.FileFor(cue);

        Assert.NotNull(path);
        Assert.Equal(expected, Path.GetFileName(path));
    }

    /// <summary>
    /// The same bytes the Linux build plays, so the two apps sound alike — which means these must
    /// actually be playable audio and not, say, a Git LFS pointer that got committed instead.
    /// </summary>
    [Theory]
    [InlineData(AgentCue.Started)]
    [InlineData(AgentCue.Finished)]
    public void TheFilesAreRealWaveAudio(AgentCue cue)
    {
        var bytes = File.ReadAllBytes(AgentSounds.FileFor(cue)!);

        Assert.True(bytes.Length > 1000, "a cue this short is not audio");
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(bytes, 8, 4));
    }

    // ── Being switched off, and having nothing to play on ───────────────────────────────────────

    /// <summary>Off means off: nothing is built, so nothing can be left running.</summary>
    [Fact]
    public void SwitchedOffPlaysNothingAndRaisesNothing()
    {
        var asked = 0;
        var previous = AgentSounds.IsEnabled;
        try
        {
            AgentSounds.IsEnabled = () => { asked++; return false; };

            AgentSounds.Play(AgentCue.Started);
            AgentSounds.Play(AgentCue.Finished);

            Assert.Equal(2, asked);   // asked per cue, so Settings takes effect on the next one
        }
        finally
        {
            AgentSounds.IsEnabled = previous;
        }
    }

    /// <summary>
    /// A missing file is a SILENCE, not an error. An app that made noise about not being able to make a
    /// noise would be worse than quiet.
    /// </summary>
    [Fact]
    public void AMissingFileIsSilentRatherThanAFailure()
    {
        var sounds = Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds");
        var file = Path.Combine(sounds, "agent-start.wav");
        var hidden = file + ".hidden";

        File.Move(file, hidden, overwrite: true);
        try
        {
            Assert.Null(AgentSounds.FileFor(AgentCue.Started));
            AgentSounds.Play(AgentCue.Started);   // no throw
        }
        finally
        {
            File.Move(hidden, file, overwrite: true);
        }
    }
}
