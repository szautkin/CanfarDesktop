using CanfarDesktop.Models;
using CanfarDesktop.Services.Cutouts;

namespace CanfarDesktop.Services;

/// <summary>
/// The search the person last ran, for the screens that follow from it.
///
/// <para>A cutout of an observation starts from where its search looked and what wavelengths it
/// asked for. The observation view and the agent's cutout tools both want that, and neither should
/// reach into the Search page to get it — so Search leaves its form here when it runs, and they read
/// it from here.</para>
/// </summary>
public sealed class SearchContext
{
    private volatile SearchFormState? _last;

    /// <summary>The form as it stood when the last search ran; null before any.</summary>
    public SearchFormState? LastSearch => _last;

    public void Searched(SearchFormState state) => _last = state;

    /// <summary>What a cutout can start from, per <see cref="CutoutHints.From"/>.</summary>
    public CutoutHints? CutoutHints => Cutouts.CutoutHints.From(_last);
}
