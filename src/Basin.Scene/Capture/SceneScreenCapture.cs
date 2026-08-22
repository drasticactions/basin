using Basin.Capabilities;

namespace Basin.Scene;

public sealed class SceneScreenCapture : IScreenCapture
{
    private readonly Scene _scene;
    private readonly OutputLayout _layout;
    private CaptureCursorState _cursor;
    private IBuffer? _cursorBuffer;

    public SceneScreenCapture(Scene scene, OutputLayout layout)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(layout);
        _scene = scene;
        _layout = layout;
    }

    public IRenderer? Renderer { get; set; }

    public RenderColor Background { get; set; }

    public IToplevelModel? Toplevels { get; set; }

    public Func<ulong, ToplevelCaptureTrees>? ToplevelContent { get; set; }

    private readonly CaptureDamageObservers _damageObservers = new();

    public void AddDamageObserver(ICaptureDamageObserver observer) => _damageObservers.Add(observer);

    public void RemoveDamageObserver(ICaptureDamageObserver observer) => _damageObservers.Remove(observer);

    public void NotifyDamaged(IOutput output, Box damage)
    {
        ArgumentNullException.ThrowIfNull(output);
        _damageObservers.Damaged(output, damage);
    }

    public void SetCursor(IBuffer? image, in CaptureCursorState state)
    {
        var next = image is null || state.Width <= 0 || state.Height <= 0
            ? default
            : state with { IsVisible = true };
        if (ReferenceEquals(image, _cursorBuffer) && next == _cursor)
        {
            return;
        }

        _cursorBuffer = image;
        _cursor = next;
        _damageObservers.CursorChanged();
    }

    public bool Supports(in CaptureSource source) => Renderer is null ? false : source.Kind switch
    {
        CaptureSourceKind.Output => true,
        CaptureSourceKind.Cursor => true,
        CaptureSourceKind.Toplevel => Toplevels is not null,
        _ => false,
    };

    public bool TryDescribe(in CaptureSource source, out CaptureFormat format)
    {
        format = default;
        switch (source.Kind)
        {
            case CaptureSourceKind.Output:
                if (source.OutputTarget is not { } output)
                {
                    return false;
                }

                var mode = output.CurrentMode;
                format = new CaptureFormat(mode.Width, mode.Height, DrmFormat.Xrgb8888);
                return true;

            case CaptureSourceKind.Cursor:
                if (!_cursor.IsVisible)
                {
                    return false;
                }

                format = new CaptureFormat(_cursor.Width, _cursor.Height, DrmFormat.Argb8888);
                return true;

            case CaptureSourceKind.Toplevel:
                if (!TryToplevelBox(source.ToplevelId, out var box, out var scale))
                {
                    return false;
                }

                var physical = OutputScaling.ToPhysical(box, scale);
                format = new CaptureFormat(physical.Width, physical.Height, DrmFormat.Xrgb8888);
                return true;

            default:
                return false;
        }
    }

    public bool Capture(in CaptureSource source, in Box region, IBuffer target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return source.Kind switch
        {
            CaptureSourceKind.Output => CaptureOutput(source.OutputTarget!, region, target, source.OverlayCursor),
            CaptureSourceKind.Toplevel => CaptureToplevel(source.ToplevelId, region, target),
            CaptureSourceKind.Cursor => CaptureCursor(target),
            _ => false,
        };
    }

    public bool TryCursorState(IOutput output, out CaptureCursorState cursor)
    {
        ArgumentNullException.ThrowIfNull(output);
        var projection = OutputProjection.For(output);
        if (!TryCursorPixels(output, projection, out var x, out var y))
        {
            cursor = default;
            return false;
        }

        var (mappedX, mappedY) = projection.MapPoint(x, y);
        cursor = _cursor with { X = mappedX, Y = mappedY };
        return true;
    }

    private bool TryCursorPixels(IOutput output, in OutputProjection projection, out int x, out int y)
    {
        x = 0;
        y = 0;
        if (!_cursor.IsVisible)
        {
            return false;
        }

        var box = _layout.BoxOf(output);
        var scale = output.Scale;
        x = (int)Math.Round((_cursor.X - box.X) * scale);
        y = (int)Math.Round((_cursor.Y - box.Y) * scale);
        var left = x - _cursor.HotspotX;
        var top = y - _cursor.HotspotY;
        return left + _cursor.Width > 0 && top + _cursor.Height > 0 &&
            left < projection.Width && top < projection.Height;
    }

    private bool CaptureOutput(IOutput output, in Box region, IBuffer target, bool overlayCursor)
    {
        var box = _layout.BoxOf(output);
        var projection = OutputProjection.For(output).CroppedTo(region.X, region.Y);
        if (!RenderAt(box.X, box.Y, projection, target))
        {
            return false;
        }

        return !overlayCursor || DrawCursorOver(output, projection, target);
    }

    private bool DrawCursorOver(IOutput output, in OutputProjection projection, IBuffer target)
    {
        if (_cursorBuffer is not { } image ||
            Renderer is not { } renderer ||
            !TryCursorPixels(output, projection, out var x, out var y))
        {
            return true;
        }

        using var texture = renderer.ImportTexture(image);
        if (texture is null)
        {
            return true;
        }

        var pass = renderer.BeginBufferPass(target, new RenderPassOptions());
        pass.AddTexture(texture, new TextureRenderOptions
        {
            SrcBox = new FBox(0, 0, _cursor.Width, _cursor.Height),
            DstBox = new Box(x - _cursor.HotspotX, y - _cursor.HotspotY, _cursor.Width, _cursor.Height),
            Transform = projection.MapsPixels ? projection.Matrix : RenderTransform.Identity,
        });
        return pass.Submit();
    }

    private bool CaptureToplevel(ulong toplevelId, in Box region, IBuffer target)
    {
        if (!TryToplevelBox(toplevelId, out var box, out var scale))
        {
            return false;
        }

        if (Renderer is { } renderer && ToplevelContent?.Invoke(toplevelId) is { Content: { } content } trees)
        {
            return _scene.RenderSubtrees(
                renderer, [content, trees.Popups], target, region.X, region.Y, scale, Background);
        }

        return RenderAt(box.X + region.X, box.Y + region.Y, scale, target);
    }

    private bool CaptureCursor(IBuffer target)
    {
        if (_cursorBuffer is not { } cursor || !_cursor.IsVisible)
        {
            return false;
        }

        if (Renderer is not { } renderer)
        {
            return false;
        }

        using var texture = renderer.ImportTexture(cursor);
        if (texture is null)
        {
            return false;
        }

        var width = Math.Min(target.Width, _cursor.Width);
        var height = Math.Min(target.Height, _cursor.Height);
        var pass = renderer.BeginBufferPass(target, new RenderPassOptions());
        pass.AddTexture(texture, new TextureRenderOptions
        {
            SrcBox = new FBox(0, 0, width, height),
            DstBox = new Box(0, 0, width, height),
        });
        return pass.Submit();
    }

    private bool RenderAt(int originX, int originY, double scale, IBuffer target) =>
        RenderAt(originX, originY, new OutputProjection(scale), target);

    private bool RenderAt(int originX, int originY, in OutputProjection projection, IBuffer target)
    {
        if (Renderer is not { } renderer)
        {
            return false;
        }

        _scene.Root.SetPosition(-originX, -originY);
        try
        {
            return _scene.Render(renderer, target, new SceneRenderOptions
            {
                Background = Background,
                Projection = projection,
            });
        }
        finally
        {
            _scene.Root.SetPosition(0, 0);
        }
    }

    private bool TryToplevelBox(ulong toplevelId, out Box box, out double scale)
    {
        box = default;
        scale = 1;
        if (Toplevels is not { } model || !model.TryGet(toplevelId, out var info) || info.Geometry.IsEmpty)
        {
            return false;
        }

        box = info.Geometry;

        foreach (var (output, _) in _layout.Outputs)
        {
            if (!_layout.BoxOf(output).Intersect(box).IsEmpty)
            {
                scale = Math.Max(scale, output.Scale);
            }
        }

        return true;
    }
}
