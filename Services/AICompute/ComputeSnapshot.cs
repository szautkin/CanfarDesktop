using CanfarDesktop.Helpers;
using CanfarDesktop.Models;

namespace CanfarDesktop.Services.AICompute;

/// <summary>
/// Where remote compute stands: its state, its session if there is one, what it launches, and whether
/// this install has it set up at all.
///
/// <para>Set up and running are separate facts. A session can be on the account with nothing set up
/// here — left from another install, or from before a reinstall — and it still holds the person's
/// cores, so it is reported, not folded into "not set up".</para>
/// </summary>
public sealed record ComputeSnapshot(ComputeState State, Session? Session, string Image, int Cores, int Ram, bool Configured);
