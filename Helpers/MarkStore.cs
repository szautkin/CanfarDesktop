using CanfarDesktop.Models;

namespace CanfarDesktop.Helpers;

/// <summary>
/// The two things a mark editor needs of a store: read a file's marks, and write them back.
///
/// Narrower than the full annotation store on purpose. That one also adds, updates and removes single
/// marks by id and lists every target — all of which an agent's tools need and none of which the drawing
/// side does. Depending on the whole of it would mean a test double had to implement four methods it
/// would never call, and would let this side quietly start using them.
/// </summary>
public interface IMarkStore
{
    /// <summary>The marks on one target, in creation order.</summary>
    IReadOnlyList<Annotation> LoadFor(string target);

    /// <summary>Replace one target's marks. Returns what is now stored.</summary>
    IReadOnlyList<Annotation> SaveFor(string target, IReadOnlyList<Annotation> annotations);
}
