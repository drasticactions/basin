namespace Basin.Scene;

public sealed class SceneTransform : SceneTree
{
    private RenderTransform _matrix = RenderTransform.Identity;
    private RenderTransform _inverse = RenderTransform.Identity;
    private bool _invertible = true;

    public SceneTransform(SceneTree parent)
        : base(parent)
    {
    }

    public RenderTransform Matrix
    {
        get => _matrix;
        set
        {
            if (_matrix == value)
            {
                return;
            }

            DamageSubtree();
            _matrix = value;
            _invertible = value.TryInvert(out _inverse);
            DamageSubtree();
        }
    }

    private IMeshTransform? _deformer;

    public IMeshTransform? Deformer
    {
        get => _deformer;
        set
        {
            if (ReferenceEquals(_deformer, value))
            {
                return;
            }

            DamageSubtree();
            _deformer = value;
            InvalidateCapture();
            if (value is null)
            {
                DropCapture();
            }

            DamageSubtree();
        }
    }

    public bool IsInertNow => _matrix.IsIdentity && Alpha >= 1f && _deformer is null;

    private MemoryBuffer? _capture;
    private bool _captureValid;
    private Box _captureBounds;
    private double _captureScale;
    private ITexture? _captureTexture;
    private IRenderer? _captureTextureRenderer;

    internal void InvalidateCapture() => _captureValid = false;

    public void NotifyDeformed() => DamageSubtree();

    public Box ContentBounds => ChildBounds();

    internal Box ChildBounds() => base.SubtreeBounds();

    internal (MemoryBuffer? Buffer, Box Bounds, double Scale) Capture =>
        (_captureValid ? _capture : null, _captureBounds, _captureScale);

    internal ITexture? GetCaptureTexture(IRenderer renderer)
    {
        if (_capture is null)
        {
            return null;
        }

        if (_captureTexture is not null && ReferenceEquals(_captureTextureRenderer, renderer))
        {
            return _captureTexture;
        }

        DropCaptureTexture();
        _captureTexture = renderer.ImportTexture(_capture);
        _captureTextureRenderer = _captureTexture is null ? null : renderer;
        return _captureTexture;
    }

    internal MemoryBuffer? EnsureCapture(IRenderer renderer, Scene scene, in Box bounds, double scale)
    {
        var width = Math.Max(1, (int)Math.Ceiling(bounds.Width * scale));
        var height = Math.Max(1, (int)Math.Ceiling(bounds.Height * scale));
        if (_capture is null || _capture.Width != width || _capture.Height != height ||
            _captureBounds != bounds || _captureScale != scale)
        {
            DropCapture();
            _capture = new MemoryBuffer(width, height, DrmFormat.Argb8888);
            _captureBounds = bounds;
            _captureScale = scale;
            _captureValid = false;
        }

        if (!_captureValid)
        {
            if (!scene.RenderCapture(renderer, this, _capture, bounds, scale))
            {
                DropCapture();
                return null;
            }

            (_captureTexture as IRefreshableTexture)?.MarkDirty();
            _captureValid = true;
        }

        return _capture;
    }

    internal SceneBuffer? ZeroCopySource()
    {
        SceneBuffer? only = null;
        foreach (var child in Children)
        {
            if (!child.Enabled)
            {
                continue;
            }

            if (only is not null || child is not SceneBuffer buffer)
            {
                return null;
            }

            only = buffer;
        }

        if (only is null || only.Buffer is not { } content || only.IsClipped)
        {
            return null;
        }

        var (width, height) = only.Size;
        if (!only.SourceBox.IsEmpty || width != content.Width || height != content.Height)
        {
            return null;
        }

        return only;
    }

    private void DropCaptureTexture()
    {
        _captureTexture?.Dispose();
        _captureTexture = null;
        _captureTextureRenderer = null;
    }

    private void DropCapture()
    {
        DropCaptureTexture();
        _capture?.Destroy();
        _capture = null;
        _captureValid = false;
    }

    protected override void OnDestroy()
    {
        DropCapture();
        base.OnDestroy();
    }

    internal bool TryMapToLocal(double x, double y, out double localX, out double localY)
    {
        if (_matrix.IsIdentity)
        {
            localX = x;
            localY = y;
            return true;
        }

        if (!_invertible)
        {
            localX = 0;
            localY = 0;
            return false;
        }

        (localX, localY) = _inverse.Map(x, y);
        return true;
    }

    internal override Box SubtreeBounds()
    {
        var bounds = base.SubtreeBounds();
        if (bounds.IsEmpty)
        {
            return bounds;
        }

        if (_deformer is { } deformer)
        {
            bounds = deformer.MapBounds(bounds);
            if (bounds.IsEmpty)
            {
                return bounds;
            }
        }

        if (_matrix.IsIdentity)
        {
            return bounds;
        }

        return _matrix.TryMapBounds(bounds, out var hull) ? hull : default;
    }

    internal override Box UntransformedSubtreeBounds()
    {
        var bounds = base.SubtreeBounds();
        if (bounds.IsEmpty || _deformer is null)
        {
            return bounds;
        }

        return _deformer.MapBounds(bounds);
    }

    internal override void DamageInto(Scene scene, int sceneX, int sceneY)
    {
        if (_deformer is null && _matrix.IsIdentity)
        {
            base.DamageInto(scene, sceneX, sceneY);
            return;
        }

        var bounds = SubtreeBounds();
        if (!bounds.IsEmpty)
        {
            scene.NotifyDamage(this, bounds.Translated(sceneX, sceneY));
        }
    }
}
