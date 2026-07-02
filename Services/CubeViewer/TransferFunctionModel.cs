using System.Numerics;

namespace CanfarDesktop.Services.CubeViewer;

/// <summary>
/// State + edit rules for the opacity transfer-function editor: control points
/// (value ∈ [0,1] → alpha ∈ [0,1]) with add / drag / remove semantics, feeding
/// <see cref="CubeColormaps.TransferRamp"/>. The Windows analogue of the macOS
/// <c>TransferFunctionEditor</c> gesture math: the two extreme-X endpoints are
/// pinned in value (they move only in alpha) so the curve always spans the full
/// [0,1] domain. Pure (no WinUI) so it is unit-testable.
/// </summary>
internal sealed class TransferFunctionModel
{
    private readonly List<Vector2> _points;

    /// <summary>Current control points (unsorted; <see cref="CubeColormaps.TransferRamp"/> sorts).</summary>
    public IReadOnlyList<Vector2> Points => _points;

    public TransferFunctionModel(IEnumerable<Vector2> points) => _points = points.ToList();

    /// <summary>A fresh editor seeded with the renderer's default ramp.</summary>
    public static TransferFunctionModel CreateDefault() => new(CubeColormaps.DefaultTransferFunction);

    /// <summary>Index of the point with the smallest X (the left endpoint).</summary>
    public int MinXIndex
    {
        get { int m = 0; for (int i = 1; i < _points.Count; i++) if (_points[i].X < _points[m].X) m = i; return m; }
    }

    /// <summary>Index of the point with the largest X (the right endpoint).</summary>
    public int MaxXIndex
    {
        get { int m = 0; for (int i = 1; i < _points.Count; i++) if (_points[i].X > _points[m].X) m = i; return m; }
    }

    /// <summary>Whether a point is one of the two X-pinned endpoints.</summary>
    public bool IsEndpoint(int index) => index == MinXIndex || index == MaxXIndex;

    /// <summary>
    /// Nearest point within an elliptical radius (rx, ry) of (x, y) — all in normalized [0,1]
    /// space; the per-axis radii let the caller express a circular hit target in PIXELS on a
    /// non-square canvas. Returns null when nothing is in range.
    /// </summary>
    public int? HitTest(float x, float y, float rx, float ry)
    {
        if (rx <= 0 || ry <= 0) return null;
        int? best = null;
        float bestD = float.MaxValue;
        for (int i = 0; i < _points.Count; i++)
        {
            float dx = (_points[i].X - x) / rx, dy = (_points[i].Y - y) / ry;
            float d = dx * dx + dy * dy;
            if (d <= 1f && d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    /// <summary>Move a point to (x, y), clamped to [0,1]; endpoints are locked in X.</summary>
    public void Drag(int index, float x, float y)
    {
        if (index < 0 || index >= _points.Count) return;
        float nx = IsEndpoint(index) ? _points[index].X : Math.Clamp(x, 0f, 1f);
        _points[index] = new Vector2(nx, Math.Clamp(y, 0f, 1f));
    }

    /// <summary>Add a control point at (x, y) (clamped to [0,1]) and return its index.</summary>
    public int Add(float x, float y)
    {
        _points.Add(new Vector2(Math.Clamp(x, 0f, 1f), Math.Clamp(y, 0f, 1f)));
        return _points.Count - 1;
    }

    /// <summary>Remove a control point. Refused (false) for endpoints so the curve keeps spanning [0,1].</summary>
    public bool Remove(int index)
    {
        if (index < 0 || index >= _points.Count || IsEndpoint(index)) return false;
        _points.RemoveAt(index);
        return true;
    }

    /// <summary>Restore the default curve.</summary>
    public void Reset()
    {
        _points.Clear();
        _points.AddRange(CubeColormaps.DefaultTransferFunction);
    }
}
