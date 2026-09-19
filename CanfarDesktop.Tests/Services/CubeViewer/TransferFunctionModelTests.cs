using System.Numerics;
using Xunit;
using CanfarDesktop.Services.CubeViewer;

namespace CanfarDesktop.Tests.Services.CubeViewer;

/// <summary>
/// Edit rules for the opacity transfer-function editor (add / drag / remove with X-pinned
/// endpoints) plus the alpha-ramp interpolation the renderer samples. Pure + deterministic.
/// </summary>
public class TransferFunctionModelTests
{
    // ── Defaults / endpoints ──

    [Fact]
    public void CreateDefault_MatchesRendererDefaultRamp()
    {
        var m = TransferFunctionModel.CreateDefault();

        Assert.Equal(CubeColormaps.DefaultTransferFunction, m.Points);
    }

    [Fact]
    public void Endpoints_AreTheExtremeXPoints()
    {
        var m = new TransferFunctionModel([new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(1f, 1f)]);

        Assert.Equal(1, m.MinXIndex);
        Assert.Equal(2, m.MaxXIndex);
        Assert.True(m.IsEndpoint(1));
        Assert.True(m.IsEndpoint(2));
        Assert.False(m.IsEndpoint(0));
    }

    // ── Replace (the MCP set_cube_transfer path) ──

    [Fact]
    public void Replace_SwapsTheWholeCurve_Clamped()
    {
        var m = TransferFunctionModel.CreateDefault();

        Assert.True(m.Replace([new Vector2(-0.5f, 2f), new Vector2(0.4f, 0.9f), new Vector2(1f, 1f)]));
        Assert.Equal(3, m.Points.Count);
        Assert.Equal(new Vector2(0f, 1f), m.Points[0]);     // clamped to [0,1]
        Assert.Equal(new Vector2(0.4f, 0.9f), m.Points[1]);
        Assert.True(m.IsEndpoint(0));                        // min/max-X of the NEW curve are the endpoints
        Assert.True(m.IsEndpoint(2));
    }

    [Fact]
    public void Replace_FewerThanTwoPoints_RefusedCurveUntouched()
    {
        var m = TransferFunctionModel.CreateDefault();
        var before = m.Points.ToArray();

        Assert.False(m.Replace([new Vector2(0.5f, 0.5f)]));
        Assert.False(m.Replace([]));
        Assert.Equal(before, m.Points);
    }

    // ── HitTest ──

    [Fact]
    public void HitTest_ReturnsNearestPointWithinRadius()
    {
        var m = new TransferFunctionModel([new Vector2(0f, 0f), new Vector2(0.5f, 0.5f), new Vector2(1f, 1f)]);

        Assert.Equal(1, m.HitTest(0.52f, 0.5f, 0.05f, 0.05f));
        Assert.Null(m.HitTest(0.25f, 0.9f, 0.05f, 0.05f)); // nothing in range
    }

    [Fact]
    public void HitTest_UsesPerAxisRadii()
    {
        // A wide-but-flat ellipse: 0.1 away in Y misses when ry is small even though rx is large.
        var m = new TransferFunctionModel([new Vector2(0.5f, 0.5f)]);

        Assert.Null(m.HitTest(0.5f, 0.6f, 0.3f, 0.05f));
        Assert.Equal(0, m.HitTest(0.5f, 0.6f, 0.3f, 0.2f));
    }

    // ── Drag ──

    [Fact]
    public void Drag_MovesInteriorPoint_Clamped()
    {
        var m = new TransferFunctionModel([new Vector2(0f, 0f), new Vector2(0.5f, 0.5f), new Vector2(1f, 1f)]);

        m.Drag(1, 1.5f, -0.2f);

        Assert.Equal(new Vector2(1f, 0f), m.Points[1]);
    }

    [Fact]
    public void Drag_EndpointLockedInX_MovesOnlyAlpha()
    {
        var m = TransferFunctionModel.CreateDefault();
        int left = m.MinXIndex, right = m.MaxXIndex;

        m.Drag(left, 0.5f, 0.3f);
        m.Drag(right, 0.2f, 0.7f);

        Assert.Equal(0f, m.Points[left].X);   // pinned
        Assert.Equal(0.3f, m.Points[left].Y);
        Assert.Equal(1f, m.Points[right].X);  // pinned
        Assert.Equal(0.7f, m.Points[right].Y);
    }

    // ── Add / Remove / Reset ──

    [Fact]
    public void Add_AppendsClampedPoint_AndReturnsItsIndex()
    {
        var m = TransferFunctionModel.CreateDefault();
        int before = m.Points.Count;

        int idx = m.Add(0.3f, 1.2f);

        Assert.Equal(before, idx);
        Assert.Equal(before + 1, m.Points.Count);
        Assert.Equal(new Vector2(0.3f, 1f), m.Points[idx]);
    }

    [Fact]
    public void Remove_RefusesEndpoints_RemovesInterior()
    {
        var m = new TransferFunctionModel([new Vector2(0f, 0f), new Vector2(0.5f, 0.5f), new Vector2(1f, 1f)]);

        Assert.False(m.Remove(m.MinXIndex));
        Assert.False(m.Remove(m.MaxXIndex));
        Assert.True(m.Remove(1));
        Assert.Equal(2, m.Points.Count);
    }

    [Fact]
    public void Reset_RestoresDefaultCurve()
    {
        var m = TransferFunctionModel.CreateDefault();
        m.Add(0.3f, 0.9f);
        m.Drag(m.MinXIndex, 0f, 1f);

        m.Reset();

        Assert.Equal(CubeColormaps.DefaultTransferFunction, m.Points);
    }

    // ── TransferRamp (what the edited points actually feed the renderer) ──

    [Fact]
    public void TransferRamp_LinearCurve_InterpolatesLinearly()
    {
        var ramp = CubeColormaps.TransferRamp([new Vector2(0f, 0f), new Vector2(1f, 1f)]);

        Assert.Equal(0, ramp[0]);
        Assert.Equal(255, ramp[255]);
        Assert.Equal(128, ramp[128]); // 128/255 → 0.502 → byte 128
    }

    [Fact]
    public void TransferRamp_HoldsFirstAlpha_BelowTheFirstPoint()
    {
        var ramp = CubeColormaps.TransferRamp([new Vector2(0.5f, 0.2f), new Vector2(1f, 1f)]);

        Assert.Equal(51, ramp[0]);   // 0.2 → 51
        Assert.Equal(51, ramp[64]);
        Assert.Equal(255, ramp[255]);
    }
}
