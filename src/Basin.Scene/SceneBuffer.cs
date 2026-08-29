using Basin.Diagnostics;
using Pixman;
using static Basin.Scene.SceneLog;

namespace Basin.Scene;

public sealed class SceneBuffer : SceneNode
{
    private BufferLock _lock;
    private ITexture? _texture;
    private bool _importFailed;
    private ICrossDeviceConversion? _conversion;
    private FBox _sourceBox;
    private int _destinationWidth;
    private int _destinationHeight;

    public SceneBuffer(SceneTree parent)
        : base(parent)
    {
    }

    public IBuffer? Buffer => _lock.Buffer;

    public Surface? InputSurface { get; set; }

    public bool InputEnabled { get; set; } = true;

    public Box? InputBox { get; set; }

    public int AcquireFenceFd { get; set; } = -1;

    public FBox SourceBox
    {
        get => _sourceBox;
        set
        {
            if (_sourceBox != value)
            {
                _sourceBox = value;
                DamageSubtree();
            }
        }
    }

    public int DestinationWidth
    {
        get => _destinationWidth;
        set => ResizeDestination(ref _destinationWidth, value);
    }

    public int DestinationHeight
    {
        get => _destinationHeight;
        set => ResizeDestination(ref _destinationHeight, value);
    }

    public bool IsOpaque { get; set; }

    private PixmanRegion32? _opaqueRegion;

    internal PixmanRegion32? OpaqueRegion => _opaqueRegion is { IsEmpty: false } region ? region : null;

    public void SetOpaqueRegion(PixmanRegion32? region)
    {
        if (region is null || region.IsEmpty)
        {
            if (_opaqueRegion is { IsEmpty: false })
            {
                _opaqueRegion.Clear();
                DamageSubtree();
            }

            return;
        }

        _opaqueRegion ??= new PixmanRegion32();
        if (_opaqueRegion.Equals(region))
        {
            return;
        }

        _opaqueRegion.Copy(region);
        DamageSubtree();
    }

    private IColorLut? _lut;

    public IColorLut? Lut
    {
        get => _lut;
        set
        {
            if (!ReferenceEquals(_lut, value))
            {
                _lut = value;
                DamageSubtree();
            }
        }
    }

    private IPixelShader? _textureShader;

    public IPixelShader? TextureShader
    {
        get => _textureShader;
        set
        {
            if (!ReferenceEquals(_textureShader, value))
            {
                _textureShader = value;
                DamageSubtree();
            }
        }
    }

    private IBackdropEffect? _backdropEffect;
    private PixmanRegion32? _backdropRegion;

    public IBackdropEffect? BackdropEffect => _backdropEffect;

    public object? BackdropKey { get; private set; }

    internal PixmanRegion32? BackdropRegion => _backdropRegion;

    internal bool HasActiveBackdrop => _backdropEffect is not null && _backdropRegion is { IsEmpty: false };

    public void SetBackdropEffect(IBackdropEffect? effect, PixmanRegion32? region, object? key = null)
    {
        var wasActive = HasActiveBackdrop;
        _backdropEffect = effect;
        BackdropKey = key;
        if (effect is null || region is null || region.IsEmpty)
        {
            _backdropRegion?.Clear();
        }
        else
        {
            _backdropRegion ??= new PixmanRegion32();
            _backdropRegion.Copy(region);
        }

        if (wasActive || HasActiveBackdrop)
        {
            DamageSubtree();
        }
    }

    public bool AcceptsInputAt(double x, double y)
    {
        if (!InputEnabled || _lock.Buffer is not { } buffer)
        {
            return false;
        }

        var (width, height) = ContentSize;
        if (x < 0 || y < 0 || x >= width || y >= height)
        {
            return false;
        }

        if (InputBox is { } box && (x < box.X || y < box.Y || x >= box.Right || y >= box.Bottom))
        {
            return false;
        }

        if (InputSurface is not { } surface)
        {
            return true;
        }

        return surface.Current.InputIsInfinite ||
               (surface.Current.HasInput && surface.Current.Input.Contains((int)x, (int)y));
    }

    public void SetBuffer(IBuffer? buffer)
    {
        if (ReferenceEquals(buffer, _lock.Buffer))
        {
            return;
        }

        var previous = _lock.Buffer;
        var hadBuffer = previous is not null;
        var sizeChanges = buffer is null || !hadBuffer ||
            buffer.Width != _lock.Buffer!.Width || buffer.Height != _lock.Buffer.Height;
        if (sizeChanges && hadBuffer)
        {
            DamageSubtree();
        }

        ReleaseTexture();
        _importFailed = false;
        _conversion = null;
        var taken = buffer is null ? default : buffer.Lock();
        _lock.Dispose();
        _lock = taken;
        _bufferSwapped = true;

        _adoptFrom = previous;
        _adoptDamage = default;
        _adoptDamageIsFull = false;
        if (sizeChanges && buffer is not null)
        {
            DamageSubtree();
        }
    }

    public void NotifyContentChanged()
    {
        _conversion?.Refresh();
        (CurrentTexture as IRefreshableTexture)?.MarkDirty();
        DamageSubtree();
    }

    public bool PreciseDamage { get; set; }

    public void NotifyContentChanged(PixmanRegion32 damage)
    {
        var none = default(DamageRects);
        NotifyContentChanged(damage, in none);
    }

    public void NotifyContentChanged(PixmanRegion32 damage, in DamageRects rects)
    {
        _conversion?.Refresh();
        if (damage.IsEmpty)
        {
            return;
        }

        var swapped = _bufferSwapped;
        _bufferSwapped = false;

        var scaled = !PreciseDamage && (_sourceBox != default ||
            (_destinationWidth > 0 && _lock.Buffer is { } b && (_destinationWidth != b.Width || _destinationHeight != b.Height)));

        if (swapped)
        {
            (CurrentTexture as IRefreshableTexture)?.MarkDirty();

            if (scaled)
            {
                _adoptDamageIsFull = true;
            }
            else if (rects.Count > 0)
            {
                _adoptDamage.Add(in rects);
            }
            else
            {
                var box = damage.Extents;
                _adoptDamage.Add(box.X1, box.Y1, box.X2 - box.X1, box.Y2 - box.Y1);
            }
        }
        else if (scaled)
        {
            (CurrentTexture as IRefreshableTexture)?.MarkDirty();
        }
        else if (rects.Count > 0 && CurrentTexture is IRefreshableTexture refreshable)
        {
            for (var i = 0; i < rects.Count; i++)
            {
                refreshable.MarkDirty(rects[i]);
            }
        }
        else if (rects.Count == 0)
        {
            var extents = damage.Extents;
            (CurrentTexture as IRefreshableTexture)?.MarkDirty(
                new Box(extents.X1, extents.Y1, extents.X2 - extents.X1, extents.Y2 - extents.Y1));
        }

        if (scaled)
        {
            DamageSubtree();
        }
        else
        {
            DamageLocal(damage);
        }
    }

    private bool _bufferSwapped;

    private IBuffer? _adoptFrom;
    private DamageRects _adoptDamage;
    private bool _adoptDamageIsFull;

    protected override (int Width, int Height) ContentSize
    {
        get
        {
            if (_lock.Buffer is not { } buffer)
            {
                return (0, 0);
            }

            return (
                _destinationWidth > 0 ? _destinationWidth : buffer.Width,
                _destinationHeight > 0 ? _destinationHeight : buffer.Height);
        }
    }

    internal (int Width, int Height) Size => ContentSize;

    internal ITexture? GetTexture(IRenderer renderer)
    {
        if (_lock.Buffer is not { } buffer)
        {
            return null;
        }

        if (OwnerIfVisible(out _, out _) is not { } scene)
        {
            if (_texture is null && !_importFailed)
            {
                _texture = renderer.ImportTexture(buffer);
                _ownsTexture = _texture is not null;
                ReportIfUnimportable(buffer);
            }

            return _texture;
        }

        if (_importFailed)
        {
            return null;
        }

        if (_texture is not null)
        {
            return _texture;
        }

        if (_adoptFrom is { } previous &&
            scene.TryAdoptTexture(renderer, previous, buffer, in _adoptDamage, _adoptDamageIsFull))
        {
            _adoptFrom = null;
            _adoptDamage = default;
            _adoptDamageIsFull = false;
        }

        _adoptFrom = null;
        var texture = scene.TextureFor(renderer, buffer);
        if (texture is not null)
        {
            _texture = texture;
            _ownsTexture = false;
            ForgetTextureWhenBufferDies(buffer);
            return texture;
        }

        if (scene.CrossDeviceImport is { } convert && convert(buffer) is { } conversion)
        {
            _conversion = conversion;
            texture = scene.TextureFor(renderer, conversion.Buffer);
            if (texture is not null)
            {
                _texture = texture;
                _ownsTexture = false;
                ForgetTextureWhenBufferDies(conversion.Buffer);
                if (buffer.TryGetDmabuf(out var foreign))
                {
                    Log.Info(
                        $"cross-device conversion: {buffer.Width}x{buffer.Height} modifier 0x{foreign.Modifier:X} now renders via a linear copy");
                }

                return texture;
            }
        }

        ReportIfUnimportable(buffer);
        return null;
    }

    private readonly List<IBuffer> _watched = [];
    private IBuffer? _textureOwner;
    private Action? _forgetTexture;

    private void ForgetTextureWhenBufferDies(IBuffer buffer)
    {
        _textureOwner = buffer;
        for (var i = 0; i < _watched.Count; i++)
        {
            if (ReferenceEquals(_watched[i], buffer))
            {
                return;
            }
        }

        _forgetTexture ??= ForgetTexture;
        _watched.Add(buffer);
        buffer.Destroyed += _forgetTexture;
    }

    private void ForgetTexture()
    {
        for (var i = _watched.Count - 1; i >= 0; i--)
        {
            var buffer = _watched[i];
            if (!buffer.IsDestroyed)
            {
                continue;
            }

            buffer.Destroyed -= _forgetTexture;
            _watched.RemoveAt(i);
            if (ReferenceEquals(buffer, _textureOwner))
            {
                _texture = null;
                _textureOwner = null;
            }
        }
    }

    private void UnwatchBuffers()
    {
        if (_forgetTexture is { } handler)
        {
            for (var i = 0; i < _watched.Count; i++)
            {
                _watched[i].Destroyed -= handler;
            }
        }

        _watched.Clear();
        _textureOwner = null;
    }

    private void ReportIfUnimportable(IBuffer buffer)
    {
        if (_texture is not null || _importFailed)
        {
            return;
        }

        _importFailed = true;
        Log.Warn(
            $"buffer {buffer.Width}x{buffer.Height} is not importable by the renderer; content dropped");
    }

    private ITexture? CurrentTexture =>
        _texture ?? (_lock.Buffer is { } buffer ? OwnerIfVisible(out _, out _)?.PeekTexture(buffer) : null);

    protected override void OnDestroy()
    {
        ReleaseTexture();
        UnwatchBuffers();
        _lock.Dispose();
        _backdropRegion?.Dispose();
        _backdropRegion = null;
        _opaqueRegion?.Dispose();
        _opaqueRegion = null;
    }

    private void ReleaseTexture()
    {
        if (_ownsTexture)
        {
            _texture?.Dispose();
        }

        _textureOwner = null;
        _texture = null;
        _ownsTexture = false;
    }

    private bool _ownsTexture;

    private void ResizeDestination(ref int field, int value)
    {
        if (field != value)
        {
            DamageSubtree();
            field = value;
            DamageSubtree();
        }
    }
}
