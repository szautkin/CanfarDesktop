using Windows.Media.Core;
using Windows.Media.Playback;

namespace CanfarDesktop.Helpers;

/// <summary>The two moments worth a sound.</summary>
public enum AgentCue
{
    /// <summary>An agent has started calling tools.</summary>
    Started,

    /// <summary>It has gone quiet again.</summary>
    Finished,
}

/// <summary>
/// Two short sounds, so the agent's arrival and departure are noticeable without being watched for.
///
/// The indicator beside the service health says WHETHER an agent is working; this says WHEN it started
/// and stopped, to someone who is reading a paper in another window. That is the whole scope: two cues,
/// a hundredth of a second of attention each, and a switch to turn them off.
///
/// Deliberately not a general sound system. There is no queue, no mixing and no per-event volume,
/// because a second sound in this application would be one too many.
///
/// The files are the same bytes the Linux build plays, so the two apps sound alike.
/// </summary>
public static class AgentSounds
{
    /// <summary>A cue, not a notification jingle.</summary>
    private const double Volume = 0.6;

    /// <summary>
    /// One player per cue, reused — and this is the whole design, not an optimisation.
    ///
    /// A player holds a live media pipeline. The Linux build kept them in a growing list pruned by an
    /// "ended" callback that does not arrive when playback errors, when there is no backend, or when a
    /// sound is cut short: a burst of job notifications left eighteen pipelines running and the window
    /// stopped responding to its own close button. One slot per cue makes that unrepresentable —
    /// replaying seeks the existing player back to the start instead of building a second one over the
    /// top of the first.
    /// </summary>
    private static readonly MediaPlayer?[] Players = new MediaPlayer?[2];

    private static readonly object Gate = new();

    /// <summary>
    /// Whether the cues are on. Read PER CUE rather than cached, so turning them off in Settings takes
    /// effect on the next one instead of on the next launch.
    /// </summary>
    public static Func<bool> IsEnabled { get; set; } = () => true;

    private static string FileName(AgentCue cue) => cue == AgentCue.Started ? "agent-start.wav" : "agent-stop.wav";

    /// <summary>
    /// The file for a cue, or null when it is not installed. A missing sound is a SILENCE, not an error:
    /// the app has not failed at anything a person asked it to do.
    /// </summary>
    public static string? FileFor(AgentCue cue)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", FileName(cue));
            return File.Exists(path) ? path : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Play a cue, if sounds are on and the file is there.
    ///
    /// Silent and harmless in every other case — switched off, not installed, or no working audio
    /// device. An app that made noise about not being able to make a noise would be worse than quiet.
    /// </summary>
    public static void Play(AgentCue cue)
    {
        try
        {
            if (!IsEnabled()) return;
            if (FileFor(cue) is not { } path) return;

            lock (Gate)
            {
                var slot = (int)cue;
                var player = Players[slot];

                if (player is null)
                {
                    player = new MediaPlayer
                    {
                        Source = MediaSource.CreateFromUri(new Uri(path)),
                        Volume = Volume,
                        // SoundEffects, not Alerts: Alerts ducks whatever else is playing, and a cue
                        // that quiets someone's music to announce itself is worse than no cue.
                        AudioCategory = MediaPlayerAudioCategory.SoundEffects,
                    };
                    Players[slot] = player;
                }

                // Already playing: back to the start rather than a second sound over the first.
                player.PlaybackSession.Position = TimeSpan.Zero;
                player.Play();
            }
        }
        catch (Exception ex)
        {
            // Includes the machine having no audio endpoint at all, which is ordinary on a remote
            // desktop and is not something to report.
            System.Diagnostics.Debug.WriteLine($"Agent cue failed: {ex.Message}");
        }
    }
}
