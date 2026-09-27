using System.Net;
using Xunit;
using CanfarDesktop.Helpers;
using CanfarDesktop.Models.Cutouts;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>The sky laid out as a chart — north up, east left, one scale — and back again for a pointer.</summary>
public class SkyCanvasProjectionTests
{
    [Fact]
    public void NorthIsUp_AndEastIsLeft()
    {
        var proj = new SkyCanvasProjection([new(10, 41), new(11, 41), new(10, 42)], 200, 200);

        var (xWest, _) = proj.ToCanvas(new(10, 41))!.Value;
        var (xEast, _) = proj.ToCanvas(new(11, 41))!.Value;
        var (_, ySouth) = proj.ToCanvas(new(10, 41))!.Value;
        var (_, yNorth) = proj.ToCanvas(new(10, 42))!.Value;

        Assert.True(xEast < xWest);
        Assert.True(yNorth < ySouth);
    }

    [Fact]
    public void APointer_MapsBackToTheSkyUnderIt()
    {
        var proj = new SkyCanvasProjection([new(10, 41), new(11.4, 41.8)], 300, 180);
        var p = new SkyPoint(10.7, 41.3);

        var (x, y) = proj.ToCanvas(p)!.Value;

        Assert.True(SkyGeometry.Distance(p, proj.ToSky(x, y)) < 1e-9);
    }

    /// <summary>
    /// A square degree at Dec 60° is square on the canvas. The old sketch scaled RA and Dec separately,
    /// drawing it twice as wide as tall.
    /// </summary>
    [Fact]
    public void ASquareOnTheSky_IsSquareOnTheCanvas_AtHighDec()
    {
        var box = SkyRegion.Box(10, 60, 1, 1).Outline();
        var proj = new SkyCanvasProjection(box, 400, 400);
        var pts = box.Select(v => proj.ToCanvas(v)!.Value).ToList();

        var width = pts.Max(p => p.X) - pts.Min(p => p.X);
        var height = pts.Max(p => p.Y) - pts.Min(p => p.Y);

        Assert.Equal(1.0, width / height, 2);
    }
}

/// <summary>A failed request says what the service said, not only its status.</summary>
public class HttpFailureTests
{
    [Fact]
    public async Task TheServicesOwnWords_AreInTheMessage()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("UsageFault: CIRCLE does not intersect the data"),
        };

        var ex = await HttpFailure.FromAsync(response);

        Assert.Equal("HTTP 400 (Bad Request): UsageFault: CIRCLE does not intersect the data", ex.Message);
        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
    }

    [Fact]
    public void AnErrorPage_IsReducedToItsText_AndKeptShort()
    {
        Assert.Equal("Denied You may not.", HttpFailure.Clean("<html><body><h1>Denied</h1>\n  <p>You may&nbsp;not.</p></body></html>")!.Replace(' ', ' '));
        Assert.Equal(HttpFailure.MaxDetail, HttpFailure.Clean(new string('x', 5000))!.Length);
        Assert.Null(HttpFailure.Clean("   "));
    }
}
