using System.Text.RegularExpressions;
using Xunit;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models.Fits;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// The same sexagesimal split existed three times, at three fidelities, and only one of them carried
/// correctly. <see cref="WcsInfo.FormatRa"/> — the FITS crosshair readout, saved coordinates and the
/// agent capture's caption — showed 359.999999° as <c>23h59m60.00s</c>. Sixty seconds is not a time.
///
/// The presentations are genuinely three (h/m/s glyphs, colons, whole seconds); the arithmetic under
/// them is now one.
/// </summary>
public class SexagesimalCarryTests
{
    // ── The carry ────────────────────────────────────────────────────────────

    /// <summary>
    /// The value that exposed it. It rounds UP past the last centisecond of the day, so the right
    /// answer is a wrap to zero — never <c>23h59m60.00s</c>, which is what the naive split produced.
    /// </summary>
    [Fact]
    public void JustUnderAFullTurn_WrapsRatherThanShowingSixtySeconds()
    {
        Assert.Equal("00h00m00.00s", WcsInfo.FormatRa(359.999999));
        Assert.Equal("00:00:00.00", Sexagesimal.FormatRaHms(359.999999));
    }

    /// <summary>
    /// And a value just below THAT, which does not reach a full day: it carries into the last second
    /// rather than wrapping, so the carry is doing work here rather than the wrap covering for it.
    /// </summary>
    [Fact]
    public void JustBelowTheWrap_CarriesIntoTheLastSecond()
    {
        Assert.Equal("23h59m59.98s", WcsInfo.FormatRa(359.9999));
        Assert.Equal("23:59:59.98", Sexagesimal.FormatRaHms(359.9999));
    }

    /// <summary>
    /// The invariant, swept rather than sampled, because the failure was at a value nobody would have
    /// thought to try: no minutes or seconds field may ever reach 60, and an RA never reaches 24h.
    ///
    /// Asserted on the COMPONENTS rather than on the rendered string — a rendered ".60" is sixty
    /// centiseconds and perfectly valid, so matching "60" in the text tests the wrong thing.
    /// </summary>
    [Fact]
    public void NoSplitEverCarriesIntoASixtiethMinuteOrSecond()
    {
        var offenders = new List<string>();

        for (var i = 0; i < 20000; i++)
        {
            // A fine sweep, nudged just below each step so the rounding boundaries are hit too.
            var deg = i * 0.018 - (i % 3 == 0 ? 1e-9 : 0);

            foreach (var decimals in new[] { 0, 1, 2, 3 })
            {
                var ra = Sexagesimal.SplitRa(deg, decimals);
                if (ra.Units >= 24 || ra.Minutes >= 60 || ra.Seconds >= 60)
                    offenders.Add($"RA {deg} @{decimals} → {ra}");

                var dec = Sexagesimal.SplitDec(deg / 4.0 - 45.0, decimals);
                if (dec.Minutes >= 60 || dec.Seconds >= 60)
                    offenders.Add($"Dec {deg / 4.0 - 45.0} @{decimals} → {dec}");
            }
        }

        Assert.Empty(offenders);
    }

    /// <summary>
    /// And the same invariant through the three renderings, checking the SECONDS field specifically —
    /// which is where the naive split put a 60.
    /// </summary>
    [Fact]
    public void NoRenderedSecondsFieldEverReadsSixty()
    {
        // The seconds field: two digits after the last ':' or 'm' separator, before the fraction or
        // the closing glyph. Deliberately not the fraction — a rendered ".60" is sixty centiseconds.
        var secondsField = new Regex("[:m]([0-9]{2})[.°'s\"]");
        var offenders = new List<string>();

        for (var i = 0; i < 20000; i++)
        {
            var deg = i * 0.018 - (i % 3 == 0 ? 1e-9 : 0);
            if (deg is < 0 or >= 360) continue;

            foreach (var rendered in new[] { WcsInfo.FormatRa(deg), Sexagesimal.FormatRaHms(deg) })
            {
                var m = secondsField.Match(rendered);
                if (m.Success && m.Groups[1].Value == "60") offenders.Add($"RA {deg} → {rendered}");
                if (rendered.StartsWith("24")) offenders.Add($"RA {deg} → {rendered}");
            }

            var dec = deg / 4.0 - 45.0;
            foreach (var rendered in new[] { WcsInfo.FormatDec(dec), Sexagesimal.FormatDecDms(dec) })
            {
                var m = secondsField.Match(rendered);
                if (m.Success && m.Groups[1].Value == "60") offenders.Add($"Dec {dec} → {rendered}");
            }
        }

        Assert.Empty(offenders);
    }

    [Theory]
    [InlineData(89.999999)]
    [InlineData(44.9999999)]
    [InlineData(-89.999999)]
    public void DecJustUnderADegree_CarriesRatherThanShowingSixty(double deg)
    {
        Assert.DoesNotContain("60", WcsInfo.FormatDec(deg));
        Assert.DoesNotContain("60", Sexagesimal.FormatDecDms(deg));
    }

    // ── The split itself ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(0.0, 0, 0, 0, 0)]
    [InlineData(15.0, 1, 0, 0, 0)]        // one hour
    [InlineData(180.0, 12, 0, 0, 0)]
    [InlineData(359.9999, 23, 59, 59, 98)]
    public void SplitRa_DecomposesAndCarries(double deg, int h, int m, int s, int cs)
    {
        var p = Sexagesimal.SplitRa(deg, 2);
        Assert.Equal((h, m, s, cs), (p.Units, p.Minutes, p.Seconds, p.Fraction));
    }

    /// <summary>RA is wrapped rather than signed: it is a position on a circle.</summary>
    [Theory]
    [InlineData(-15.0, 23)]
    [InlineData(375.0, 1)]
    [InlineData(720.0, 0)]
    public void SplitRa_WrapsIntoRange(double deg, int expectedHours)
        => Assert.Equal(expectedHours, Sexagesimal.SplitRa(deg, 2).Units);

    /// <summary>
    /// Dec is NOT wrapped. A declination outside [-90,90] is a caller's mistake to notice, and folding
    /// it silently would hide theirs.
    /// </summary>
    [Fact]
    public void SplitDec_DoesNotFoldAnOutOfRangeValue()
        => Assert.Equal(100, Sexagesimal.SplitDec(100.0, 1).Units);

    [Theory]
    [InlineData(12.5, 1, 12, 30, 0)]
    [InlineData(-12.5, -1, 12, 30, 0)]
    [InlineData(-0.00001, -1, 0, 0, 0)]   // a hair below zero is still negative
    public void SplitDec_KeepsTheSignSeparateFromTheMagnitude(double deg, int sign, int d, int m, int s)
    {
        var p = Sexagesimal.SplitDec(deg, 1);
        Assert.Equal((sign, d, m, s), (p.Sign, p.Units, p.Minutes, p.Seconds));
    }

    /// <summary>Whole seconds is the cube axis caption's precision, and it carries the same way.</summary>
    [Fact]
    public void SplitAtZeroDecimals_CarriesToo()
    {
        var p = Sexagesimal.SplitRa(359.99, 0);
        Assert.Equal((23, 59, 58), (p.Units, p.Minutes, p.Seconds));
        Assert.Equal(0, p.Fraction);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(7)]
    public void Split_RefusesAPrecisionItCannotDoInIntegers(int decimals)
        => Assert.Throws<ArgumentOutOfRangeException>(() => Sexagesimal.SplitRa(10.0, decimals));

    // ── The presentations still differ ───────────────────────────────────────

    /// <summary>
    /// Collapsing the arithmetic must not collapse the readouts: the viewer's h/m/s glyphs and the
    /// table's colons are different on purpose, and a caller reading one expects that one.
    /// </summary>
    [Fact]
    public void TheThreePresentationsAreStillDistinct()
    {
        Assert.Equal("12h30m00.00s", WcsInfo.FormatRa(187.5));
        Assert.Equal("12:30:00.00", Sexagesimal.FormatRaHms(187.5));

        Assert.Equal("+41°16'09.0\"", WcsInfo.FormatDec(41.269166));
        Assert.Equal("+41:16:09.0", Sexagesimal.FormatDecDms(41.269166));
    }

    // ── The resolver string ──────────────────────────────────────────────────

    /// <summary>
    /// The fourth copy, and the one with teeth: the seconds field is centiseconds written as FOUR
    /// digits with no decimal point, so a carry produced <c>6000</c> — sixty seconds — in a coordinate
    /// handed to CADC's resolver. It was found by the guard below rather than by reading.
    /// </summary>
    [Fact]
    public void ResolverString_NeverCarriesIntoASixtiethSecond()
    {
        var offenders = new List<string>();

        for (var i = 0; i < 20000; i++)
        {
            var ra = i * 0.018 - (i % 3 == 0 ? 1e-9 : 0);
            if (ra is < 0 or >= 360) continue;
            var dec = ra / 4.0 - 45.0;

            var s = WcsInfo.FormatForResolver(ra, dec);
            var fields = s.Split(',');
            var raSeconds = int.Parse(fields[0].Split(':')[2]);
            var decSeconds = int.Parse(fields[1].Split(':')[2]);

            if (raSeconds >= 6000) offenders.Add($"RA {ra} → {s}");
            if (decSeconds >= 600) offenders.Add($"Dec {dec} → {s}");
        }

        Assert.Empty(offenders);
    }

    /// <summary>
    /// The shape CADC expects is unchanged — this was a fix underneath, not a reformat. RA seconds
    /// are centiseconds in four digits; Dec seconds are deci-arcseconds in three.
    /// </summary>
    [Theory]
    [InlineData(187.5, 41.269166, "12:30:0000,+41:16:090")]
    [InlineData(0.0, 0.0, "00:00:0000,+00:00:000")]
    [InlineData(0.0, -12.5, "00:00:0000,-12:30:000")]
    public void ResolverString_KeepsItsFormat(double ra, double dec, string expected)
        => Assert.Equal(expected, WcsInfo.FormatForResolver(ra, dec));

    // ── And the split stays the only copy ────────────────────────────────────

    /// <summary>
    /// A guard against the thing that caused this: the knowledge lived in three places and only two of
    /// them had the fix. Any new file doing its own degrees→h/m/s division will trip this, and the
    /// answer is to call <c>SplitRa</c>/<c>SplitDec</c> rather than to add another copy.
    /// </summary>
    [Fact]
    public void NoFileGrowsItsOwnSexagesimalSplitAgain()
    {
        var root = RepoRoot();
        // The tell-tale of a hand-rolled split: dividing degrees into hours by 15 and then truncating
        // to a whole number nearby. The window spans a few lines because the cast is never on the same
        // one — checked against the four copies this replaced, all of which it catches.
        var handRolled = new Regex(@"/\s*15\.0[\s\S]{0,200}?\(int\)");

        var offenders = Directory
            .EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.EndsWith("Sexagesimal.cs", StringComparison.Ordinal)
                     && !f.EndsWith("SexagesimalCarryTests.cs", StringComparison.Ordinal))
            .Where(f => handRolled.IsMatch(File.ReadAllText(f)))
            .Select(f => Path.GetFileName(f))
            .ToList();

        Assert.True(offenders.Count == 0,
            "these files look like they split sexagesimal by hand; call Sexagesimal.SplitRa/SplitDec instead: "
            + string.Join(", ", offenders));
    }

    /// <summary>Walk up to the directory holding the .slnx, so the sweep runs from the repo root.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !dir.EnumerateFiles("*.slnx").Any()) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
