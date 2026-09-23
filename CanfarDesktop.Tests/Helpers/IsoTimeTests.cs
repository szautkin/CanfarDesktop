using System.Globalization;
using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

public class IsoTimeTests
{
    [Fact]
    public void Of_WritesUtcToTheSecond()
        => Assert.Equal("2026-09-22T21:09:05Z",
            IsoTime.Of(new DateTimeOffset(2026, 9, 22, 21, 9, 5, TimeSpan.Zero)));

    [Fact]
    public void Of_ConvertsAnOffsetToUtc_SoTheZIsTrue()
        => Assert.Equal("2026-09-22T21:09:05Z",
            IsoTime.Of(new DateTimeOffset(2026, 9, 22, 17, 9, 5, TimeSpan.FromHours(-4))));

    [Fact]
    public void OfUtc_DoesNotShiftWhatTheCallerVouchesFor()
        => Assert.Equal("2026-09-22T21:09:05Z",
            IsoTime.OfUtc(new DateTime(2026, 9, 22, 21, 9, 5, DateTimeKind.Utc)));

    /// <summary>
    /// The bug this exists for: in a custom format string ':' is the culture's time separator, so a
    /// culture that separates with a dot turned every hand-written copy into "21.09.05".
    /// </summary>
    [Fact]
    public void Of_IsIsoUnderACultureWhoseTimeSeparatorIsADot()
    {
        var dotted = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        dotted.DateTimeFormat.TimeSeparator = ".";

        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = dotted;
            var at = new DateTimeOffset(2026, 9, 22, 21, 9, 5, TimeSpan.Zero);

            // What the old copies wrote under this culture, to prove the test can see the fault.
            Assert.Equal("2026-09-22T21.09.05Z", at.UtcDateTime.ToString(IsoTime.Format));
            Assert.Equal("2026-09-22T21:09:05Z", IsoTime.Of(at));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
