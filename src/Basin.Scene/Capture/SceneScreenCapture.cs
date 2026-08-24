using Basin.Capabilities;

namespace Basin.Scene;

public sealed class SceneScreenCapture : IScreenCapture
{
    private readonly Scene _scene;
    private readonly OutputLayout _layout;
    private readonly ExclusionWatch _watch;
    private CaptureCursorState _cursor;
    private IBuffer? _cursorBuffer;
    private IToplevelModel? _toplevels;
    private ToplevelInfo[] _exclusionScratch = new ToplevelInfo[16];
    private SceneNode?[] _hidden = new SceneNode?[16];

    public SceneScreenCapture(Scene scene, OutputLayout layout)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(layout);
        _scene = scene;
        _layout = layout;
        _watch = new ExclusionWatch(this);
    }

    public IRenderer? Renderer { get; set; }

    public RenderColor Background { get; set; }

    public IToplevelModel? Toplevels
    {
        get => _toplevels;
        set
        {
            if (ReferenceEquals(_toplevels, value))
            {
                return;
            }

            _toplevels?.RemoveObserver(_watch);
            _watch.Reset();
            _toplevels = value;
            _toplevels?.AddObserver(_watch);
        }
    }

    public ToplevelSceneIndex? Index { get; set; }

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
        CaptureSourceKind.Toplevel => Toplevels is { } model &&
            (!model.TryGet(source.ToplevelId, out var info) ||
                (info.State & ToplevelState.ExcludedFromCapture) == 0),
        CaptureSourceKind.Region => IntersectsAnyOutput(source.LayoutBox),
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

            case CaptureSourceKind.Region:
                if (!IntersectsAnyOutput(source.LayoutBox))
                {
                    return false;
                }

                var regionScale = ResolveRegionScale(source);
                var regionPhysical = OutputScaling.ToPhysical(source.LayoutBox, regionScale);
                format = new CaptureFormat(regionPhysical.Width, regionPhysical.Height, DrmFormat.Xrgb8888);
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
            CaptureSourceKind.Region => CaptureRegion(source, region, target),
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
        var hidden = HideExcluded();
        try
        {
            if (!RenderAt(box.X, box.Y, projection, target))
            {
                return false;
            }
        }
        finally
        {
            RestoreHidden(hidden);
        }

        return !overlayCursor || DrawCursorOver(output, projection, target);
    }

    private int HideExcluded()
    {
        if (Toplevels is not { } model || Index is not { } index)
        {
            return 0;
        }

        var count = model.Enumerate(_exclusionScratch);
        while (count < 0)
        {
            _exclusionScratch = new ToplevelInfo[_exclusionScratch.Length * 2];
            count = model.Enumerate(_exclusionScratch);
        }

        var hidden = 0;
        for (var i = 0; i < count; i++)
        {
            if ((_exclusionScratch[i].State & ToplevelState.ExcludedFromCapture) == 0 ||
                !index.TryGet(_exclusionScratch[i].Id, out var trees))
            {
                continue;
            }

            if (trees.Content is { Enabled: true } content)
            {
                Hide(content, ref hidden);
            }

            if (trees.Popups is { Enabled: true } popups)
            {
                Hide(popups, ref hidden);
            }
        }

        return hidden;
    }

    private void Hide(SceneNode node, ref int hidden)
    {
        if (hidden == _hidden.Length)
        {
            Array.Resize(ref _hidden, _hidden.Length * 2);
        }

        node.SetEnabledForCapture(false);
        _hidden[hidden++] = node;
    }

    private void RestoreHidden(int hidden)
    {
        for (var i = 0; i < hidden; i++)
        {
            _hidden[i]!.SetEnabledForCapture(true);
            _hidden[i] = null;
        }
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

        if (Renderer is { } renderer && Index is { } index &&
            index.TryGet(toplevelId, out var trees) && trees.Content is { } content)
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

    private bool IntersectsAnyOutput(in Box box)
    {
        foreach (var (output, _) in _layout.Outputs)
        {
            if (!_layout.BoxOf(output).Intersect(box).IsEmpty)
            {
                return true;
            }
        }

        return false;
    }

    private double ResolveRegionScale(in CaptureSource source)
    {
        if (source.Scale > 0)
        {
            return source.Scale;
        }

        double scale = 1;
        foreach (var (output, _) in _layout.Outputs)
        {
            if (!_layout.BoxOf(output).Intersect(source.LayoutBox).IsEmpty)
            {
                scale = Math.Max(scale, output.Scale);
            }
        }

        return scale;
    }

    private bool CaptureRegion(in CaptureSource source, in Box crop, IBuffer target)
    {
        if (!IntersectsAnyOutput(source.LayoutBox))
        {
            return false;
        }

        var scale = ResolveRegionScale(source);
        var box = source.LayoutBox;
        var hidden = HideExcluded();
        try
        {
            if (!RenderAt(box.X + crop.X, box.Y + crop.Y, scale, target))
            {
                return false;
            }
        }
        finally
        {
            RestoreHidden(hidden);
        }

        return !source.OverlayCursor || DrawCursorOverRegion(box, crop, scale, target);
    }

    private bool DrawCursorOverRegion(in Box box, in Box crop, double scale, IBuffer target)
    {
        if (_cursorBuffer is not { } image || Renderer is not { } renderer || !_cursor.IsVisible)
        {
            return true;
        }

        var x = (int)Math.Round((_cursor.X - box.X - crop.X) * scale);
        var y = (int)Math.Round((_cursor.Y - box.Y - crop.Y) * scale);
        var left = x - _cursor.HotspotX;
        var top = y - _cursor.HotspotY;
        if (left + _cursor.Width <= 0 || top + _cursor.Height <= 0 ||
            left >= target.Width || top >= target.Height)
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
            DstBox = new Box(left, top, _cursor.Width, _cursor.Height),
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
        if (Toplevels is not { } model || !model.TryGet(toplevelId, out var info) || info.Geometry.IsEmpty ||
            (info.State & ToplevelState.ExcludedFromCapture) != 0)
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

    private void OnExclusionFlipped(in ToplevelInfo info)
    {
        foreach (var (output, _) in _layout.Outputs)
        {
            var box = _layout.BoxOf(output);
            if (info.Geometry.IsEmpty || !box.Intersect(info.Geometry).IsEmpty)
            {
                _damageObservers.Damaged(output, new Box(0, 0, box.Width, box.Height));
            }
        }
    }

    private sealed class ExclusionWatch(SceneScreenCapture owner) : IToplevelObserver
    {
        private readonly Dictionary<ulong, bool> _excluded = [];

        public void Reset() => _excluded.Clear();

        public void OnToplevelAdded(ulong toplevelId) => Track(toplevelId, damageOnFlip: false);

        public void OnToplevelChanged(ulong toplevelId) => Track(toplevelId, damageOnFlip: true);

        public void OnToplevelRemoved(ulong toplevelId) => _excluded.Remove(toplevelId);

        private void Track(ulong toplevelId, bool damageOnFlip)
        {
            if (owner.Toplevels is not { } model || !model.TryGet(toplevelId, out var info))
            {
                return;
            }

            var excluded = (info.State & ToplevelState.ExcludedFromCapture) != 0;
            if (_excluded.TryGetValue(toplevelId, out var was) && was == excluded)
            {
                return;
            }

            _excluded[toplevelId] = excluded;
            if (damageOnFlip)
            {
                owner.OnExclusionFlipped(info);
            }
        }
    }
}
