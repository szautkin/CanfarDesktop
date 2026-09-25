namespace CanfarDesktop.Models;

// What a TAP service says about itself. Data, so it lives with the models: the ADQL validator in
// Helpers reads it, and a helper reaching up into Services for a record was the wrong way round.

/// <summary>One column of one table, as the service describes it.</summary>
/// <param name="Ucd">
/// IVOA Unified Content Descriptor — what the number MEANS, independent of its name.
/// <c>pos.eq.ra</c> identifies a right ascension whatever the column happens to be called.
/// </param>
public sealed record TapColumn(string Name, string Datatype, string Description, string Unit, string Ucd);

/// <summary>One table, with the columns it holds.</summary>
public sealed record TapTable(string Name, string Description, List<TapColumn> Columns);

/// <summary>A join the service itself declares between two tables.</summary>
public sealed record TapKey(string FromTable, string TargetTable, string FromColumn, string TargetColumn, string Description);

/// <summary>Everything the TAP service says about itself.</summary>
public sealed class TapSchema
{
    public List<TapTable> Tables { get; init; } = [];
    public List<TapKey> Keys { get; init; } = [];

    public bool IsEmpty => Tables.Count == 0;

    /// <summary>
    /// One table by name, matched case-insensitively: ADQL identifiers are case-insensitive unless
    /// quoted, and someone who read <c>caom2.Plane</c> in a listing may still type <c>caom2.plane</c>.
    /// </summary>
    public TapTable? Table(string name)
        => Tables.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Every declared join that touches <paramref name="table"/>, in either direction.</summary>
    public IReadOnlyList<TapKey> KeysTouching(string table)
        => Keys.Where(k => string.Equals(k.FromTable, table, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(k.TargetTable, table, StringComparison.OrdinalIgnoreCase)).ToList();
}
