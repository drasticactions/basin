using Basin.Scene;

namespace Basin.Plasma;

public sealed class ShadowEffect : IDisposable
{
    private readonly SceneSurface _scene;
    private readonly ShadowManager _manager;
    private readonly SceneTree _tree;
    private readonly SceneBuffer[] _cells = new SceneBuffer[8];
    private readonly Action _refresh;
    private SurfaceShadow? _shadow;
    private bool _disposed;

    public ShadowEffect(SceneSurface scene, ShadowManager manager)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(manager);
        _scene = scene;
        _manager = manager;
        _tree = new SceneTree(scene.Tree);
        _tree.LowerToBottom();
        for (var i = 0; i < _cells.Length; i++)
        {
            _cells[i] = new SceneBuffer(_tree) { InputEnabled = false };
        }

        _refresh = Refresh;
        scene.Surface.Committed += _refresh;
        scene.Destroyed += Dispose;
        Refresh();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _scene.Surface.Committed -= _refresh;
        _scene.Destroyed -= Dispose;
        if (_shadow is not null)
        {
            _shadow.Changed -= _refresh;
            _shadow = null;
        }

        if (!_tree.IsDestroyed)
        {
            _tree.Destroy();
        }
    }

    private void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        var shadow = _manager.ShadowOf(_scene.Surface);
        if (!ReferenceEquals(shadow, _shadow))
        {
            if (_shadow is not null)
            {
                _shadow.Changed -= _refresh;
            }

            _shadow = shadow;
            if (shadow is not null)
            {
                shadow.Changed += _refresh;
            }
        }

        if (shadow is null || !_scene.Surface.IsMapped)
        {
            _tree.Enabled = false;
            for (var i = 0; i < _cells.Length; i++)
            {
                _cells[i].SetBuffer(null);
            }

            return;
        }

        _tree.Enabled = true;
        Layout(shadow);
    }

    private void Layout(SurfaceShadow shadow)
    {
        var state = _scene.Surface.Current;
        var scale = Math.Max(1, state.Scale);
        var x0 = -(int)Math.Round(shadow.LeftOffset, MidpointRounding.AwayFromZero);
        var y0 = -(int)Math.Round(shadow.TopOffset, MidpointRounding.AwayFromZero);
        var x1 = state.Width + (int)Math.Round(shadow.RightOffset, MidpointRounding.AwayFromZero);
        var y1 = state.Height + (int)Math.Round(shadow.BottomOffset, MidpointRounding.AwayFromZero);

        var left = shadow.Buffer(ShadowPart.Left);
        var topLeft = shadow.Buffer(ShadowPart.TopLeft);
        var top = shadow.Buffer(ShadowPart.Top);
        var topRight = shadow.Buffer(ShadowPart.TopRight);
        var right = shadow.Buffer(ShadowPart.Right);
        var bottomRight = shadow.Buffer(ShadowPart.BottomRight);
        var bottom = shadow.Buffer(ShadowPart.Bottom);
        var bottomLeft = shadow.Buffer(ShadowPart.BottomLeft);

        var topLeftW = LogicalWidth(topLeft, scale);
        var topLeftH = LogicalHeight(topLeft, scale);
        var topRightW = LogicalWidth(topRight, scale);
        var topRightH = LogicalHeight(topRight, scale);
        var bottomLeftW = LogicalWidth(bottomLeft, scale);
        var bottomLeftH = LogicalHeight(bottomLeft, scale);
        var bottomRightW = LogicalWidth(bottomRight, scale);
        var bottomRightH = LogicalHeight(bottomRight, scale);

        SetCell(ShadowPart.TopLeft, topLeft, x0, y0, topLeftW, topLeftH);
        SetCell(ShadowPart.TopRight, topRight, x1 - topRightW, y0, topRightW, topRightH);
        SetCell(ShadowPart.BottomLeft, bottomLeft, x0, y1 - bottomLeftH, bottomLeftW, bottomLeftH);
        SetCell(ShadowPart.BottomRight, bottomRight, x1 - bottomRightW, y1 - bottomRightH, bottomRightW, bottomRightH);
        SetCell(ShadowPart.Top, top, x0 + topLeftW, y0, x1 - topRightW - (x0 + topLeftW), LogicalHeight(top, scale));
        SetCell(
            ShadowPart.Bottom, bottom, x0 + bottomLeftW, y1 - LogicalHeight(bottom, scale),
            x1 - bottomRightW - (x0 + bottomLeftW), LogicalHeight(bottom, scale));
        SetCell(ShadowPart.Left, left, x0, y0 + topLeftH, LogicalWidth(left, scale), y1 - bottomLeftH - (y0 + topLeftH));
        SetCell(
            ShadowPart.Right, right, x1 - LogicalWidth(right, scale), y0 + topRightH,
            LogicalWidth(right, scale), y1 - bottomRightH - (y0 + topRightH));
    }

    private void SetCell(ShadowPart part, IBuffer? buffer, int x, int y, int width, int height)
    {
        var cell = _cells[(int)part];
        if (buffer is null || width <= 0 || height <= 0)
        {
            cell.SetBuffer(null);
            return;
        }

        cell.SetBuffer(buffer);
        cell.SetPosition(x, y);
        cell.DestinationWidth = width;
        cell.DestinationHeight = height;
    }

    private static int LogicalWidth(IBuffer? buffer, int scale) =>
        buffer is null ? 0 : Math.Max(1, (int)Math.Round((double)buffer.Width / scale));

    private static int LogicalHeight(IBuffer? buffer, int scale) =>
        buffer is null ? 0 : Math.Max(1, (int)Math.Round((double)buffer.Height / scale));
}
