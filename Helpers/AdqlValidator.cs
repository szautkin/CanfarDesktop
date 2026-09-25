using CanfarDesktop.Models;

namespace CanfarDesktop.Helpers;

/// <summary>One thing wrong with a query, and where in the text it is.</summary>
/// <param name="Start">Character offset of the offending text, so the editor can select it.</param>
/// <param name="End">Character offset one past the offending text.</param>
/// <param name="Message">What is wrong, in the words a person needs.</param>
/// <param name="Fix">What to write instead, when there is a single obvious answer.</param>
public sealed record AdqlProblem(int Start, int End, string Message, string? Fix)
{
    public int Length => End - Start;

    /// <summary>
    /// One problem, in the words shown on the button, in the banner, and in the refusal an agent gets.
    /// One place, so the three cannot say it differently.
    /// </summary>
    public string Describe() => Fix is null ? Message : $"{Message} — write {Fix}";
}

/// <summary>
/// Catch the ADQL mistakes the service would reject, before the round trip.
///
/// Not a parser, and deliberately not: ADQL is a dialect of SQL, and a parser for it that is 95% right
/// is a parser that refuses good queries — which is worse than sending them. This reads two things out
/// of the text (what the FROM clause names, and every <c>qualifier.column</c> reference) and applies
/// the rules the service applies to those.
///
/// The rule that prompted it, confirmed against CADC:
///
/// <code>
/// FROM caom2.Observation JOIN caom2.Plane ON Plane.obsID=Observation.obsID
///   → Server error (400): Column [obsID] is ambiguous.
///
/// FROM caom2.Observation AS o JOIN caom2.Plane AS p ON p.obsID=o.obsID   → ok
/// FROM caom2.Observation JOIN caom2.Plane ON caom2.Plane.obsID=…         → ok
/// </code>
///
/// A bare table name is an acceptable qualifier when the column it names belongs to only one of the
/// joined tables — <c>Observation.observationID</c> in that same query was fine — so "always write an
/// alias" is NOT the rule, and reporting it as one would flag working queries.
///
/// <b>Only confident problems are reported.</b> Anything this cannot resolve — a subquery, a function,
/// a table the schema has not been fetched for — is left alone. A false positive here disables Execute
/// on a query that would have worked, which is a worse failure than the one being prevented.
/// </summary>
public static class AdqlValidator
{
    /// <summary>Keywords that end a FROM/JOIN clause, so a WHERE or an ON is never read as a table.</summary>
    private static readonly string[] ClauseEnders =
        ["where", "on", "group", "order", "having", "limit", "using", "select"];

    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>A table named in FROM/JOIN: how it was written, its bare name, and its alias if it has one.</summary>
    private sealed record FromEntry(string Written, string Bare, string? Alias);

    /// <summary>
    /// Everything wrong with <paramref name="adql"/>, in the order it appears. An empty list means
    /// "nothing this can be sure about" — NOT "valid".
    /// </summary>
    public static IReadOnlyList<AdqlProblem> Problems(string adql, TapSchema? schema)
    {
        // Nothing is knowable without the schema, and "unknown table" for every table is the loudest
        // possible way to say "I have not loaded yet". The service's own tables arrive asynchronously,
        // so this is the state the editor is in for the first second of every session.
        if (schema is null || schema.IsEmpty) return [];

        var from = FromEntries(adql);
        if (from.Count == 0) return [];

        var problems = new List<AdqlProblem>();

        // Unknown tables first: every later rule reads columns off them, and reporting a missing column
        // of a table that does not exist is noise.
        foreach (var entry in from)
        {
            if (schema.Table(entry.Written) is not null) continue;

            var at = FindIdentifier(adql, entry.Written);
            if (at < 0) continue;

            problems.Add(new AdqlProblem(at, at + entry.Written.Length,
                $"no table \"{entry.Written}\" in this service",
                Nearest(entry.Written, schema.Tables.Select(t => t.Name))));
        }
        if (problems.Count > 0) return problems;

        foreach (var (start, qualifier, column) in QualifiedReferences(adql))
        {
            var end = start + qualifier.Length + 1 + column.Length;

            // An alias resolves to its table; anything else must be a table name.
            var byAlias = from.FirstOrDefault(e => e.Alias is not null && Eq(e.Alias, qualifier));
            string table;

            if (byAlias is not null)
            {
                table = byAlias.Written;
            }
            else
            {
                var full = from.FirstOrDefault(e => Eq(e.Written, qualifier));
                var bare = from.FirstOrDefault(e => Eq(e.Bare, qualifier));

                if (full is not null)
                {
                    table = full.Written;
                }
                else if (bare is not null)
                {
                    // A bare table name — legal only while the column it names is unambiguous across
                    // the joined tables.
                    var owners = from.Where(o => HasColumn(schema, o.Written, column))
                                     .Select(o => o.Written).ToList();
                    if (owners.Count > 1)
                    {
                        problems.Add(new AdqlProblem(start, end,
                            $"{qualifier}.{column} is ambiguous — {column} is in {string.Join(" and ", owners)}",
                            $"{bare.Written}.{column}"));
                        continue;
                    }
                    table = bare.Written;
                }
                else
                {
                    // Not an alias and not a table in FROM: a subquery, a function, or a genuine typo.
                    // Not confident either way.
                    continue;
                }
            }

            var resolved = schema.Table(table);
            if (resolved is null) continue;

            if (!resolved.Columns.Any(c => Eq(c.Name, column)))
            {
                var nearest = Nearest(column, resolved.Columns.Select(c => c.Name));
                problems.Add(new AdqlProblem(start, end,
                    $"{table} has no column \"{column}\"",
                    nearest is null ? null : $"{qualifier}.{nearest}"));
            }
        }

        return problems;
    }

    private static bool HasColumn(TapSchema schema, string table, string column)
        => schema.Table(table)?.Columns.Any(c => Eq(c.Name, column)) == true;

    /// <summary>
    /// The closest known name to <paramref name="given"/>, when one is clearly closest.
    ///
    /// A prefix or a case difference only — not an edit distance. "Did you mean" on a guess is worse
    /// than no suggestion, and the two mistakes people actually make here are the wrong case and a
    /// truncated name.
    /// </summary>
    private static string? Nearest(string given, IEnumerable<string> known)
    {
        var lower = given.ToLowerInvariant();
        var hits = known
            .Where(k =>
            {
                var kl = k.ToLowerInvariant();
                return kl == lower || kl.StartsWith(lower, StringComparison.Ordinal) || lower.StartsWith(kl, StringComparison.Ordinal);
            })
            .Distinct(StringComparer.Ordinal)
            .Take(2)
            .ToList();

        return hits.Count == 1 ? hits[0] : null;
    }

    /// <summary>
    /// The tables a query selects from, with their aliases. Reads the words after FROM and each JOIN,
    /// stopping at the first keyword that ends the clause.
    /// </summary>
    private static List<FromEntry> FromEntries(string adql)
    {
        var entries = new List<FromEntry>();
        var words = adql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < words.Length; i++)
        {
            var w = words[i].Trim('(', ')');
            if (!Eq(w, "from") && !Eq(w, "join")) continue;

            i++;
            if (i >= words.Length) break;

            var written = words[i].Trim('(', ')', ',');
            if (written.Length == 0 || ClauseEnders.Any(k => Eq(k, written))) continue;

            // `AS x`, or a bare `x` that is not a keyword.
            string? alias = null;
            if (i + 1 < words.Length)
            {
                var next = words[i + 1];
                if (Eq(next, "as"))
                {
                    if (i + 2 < words.Length) alias = words[i + 2].Trim(',');
                    i += 2;
                }
                else
                {
                    var n = next.Trim(',');
                    if (n.Length > 0
                        && !ClauseEnders.Any(k => Eq(k, n))
                        && !Eq(n, "join") && !Eq(n, "inner") && !Eq(n, "left") && !Eq(n, "right")
                        && n.All(c => char.IsLetterOrDigit(c) || c == '_'))
                    {
                        alias = n;
                        i++;
                    }
                }
            }

            var lastDot = written.LastIndexOf('.');
            var bare = lastDot >= 0 ? written[(lastDot + 1)..] : written;
            entries.Add(new FromEntry(written, bare, alias));
        }

        return entries;
    }

    /// <summary>
    /// Every <c>qualifier.column</c> in the text, with the offset of the qualifier. A three-part
    /// <c>caom2.Plane.obsID</c> yields <c>caom2.Plane</c> as the qualifier, which is what the service
    /// treats it as.
    /// </summary>
    private static List<(int Start, string Qualifier, string Column)> QualifiedReferences(string adql)
    {
        static bool Ident(char c) => char.IsLetterOrDigit(c) || c == '_';

        var found = new List<(int, string, string)>();
        var i = 0;

        while (i < adql.Length)
        {
            if (!Ident(adql[i]) || (i > 0 && (Ident(adql[i - 1]) || adql[i - 1] == '.')))
            {
                i++;
                continue;
            }

            var start = i;
            var parts = new List<string>();
            while (true)
            {
                var s = i;
                while (i < adql.Length && Ident(adql[i])) i++;
                if (i == s) break;

                parts.Add(adql[s..i]);
                if (i < adql.Length && adql[i] == '.') i++;
                else break;
            }

            if (parts.Count >= 2)
                found.Add((start, string.Join(".", parts.Take(parts.Count - 1)), parts[^1]));
        }

        return found;
    }

    /// <summary>The identifier <paramref name="needle"/> in <paramref name="haystack"/>, as a whole word. -1 when absent.</summary>
    private static int FindIdentifier(string haystack, string needle)
    {
        var from = 0;
        while (from < haystack.Length)
        {
            var at = haystack.IndexOf(needle, from, StringComparison.OrdinalIgnoreCase);
            if (at < 0) return -1;

            var beforeOk = at == 0 || !char.IsLetterOrDigit(haystack[at - 1]);
            var after = at + needle.Length;
            var afterOk = after >= haystack.Length || !char.IsLetterOrDigit(haystack[after]);
            if (beforeOk && afterOk) return at;

            from = at + 1;
        }
        return -1;
    }
}
