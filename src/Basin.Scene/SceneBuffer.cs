using Basin.Capabilities;
using Basin.Diagnostics;
using Pixman;
using static Basin.Scene.SceneLog;

namespace Basin.Scene;

/// <summary>
/// A scene node that draws the content of an <see cref="IBuffer"/>, for example a client surface or chrome.
/// It holds a lock on its buffer and imports the texture when a renderer first draws it.
/// </summary>
public sealed class SceneBuffer : SceneNode
{
    private BufferLock _lock;
    private ITexture? _texture;
    private bool _importFailed;
    private ICrossDeviceConversion? _conversion;
    private FBox _sourceBox;
    private int _destinationWidth;
    private int _destinationHeight;
    private SceneBufferDevice? _device;

    /// <summary>
    /// Initializes a new instance of the <see cref="SceneBuffer"/> class with no buffer.
    /// </summary>
    /// <param name="parent">The tree that holds the new node.</param>
    public SceneBuffer(SceneTree parent)
        : base(parent)
    {
    }

    /// <summary>
    /// Gets the buffer that the node shows, or <see langword="null"/> when it has none. The node holds a lock on it.
    /// </summary>
    public IBuffer? Buffer => _lock.Buffer;

    /// <summary>
    /// Gets or sets the client surface that receives input on this node. <see langword="null"/> means that the node has no surface.
    /// When it is set, the input region of the surface limits hits.
    /// </summary>
    public Surface? InputSurface { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether hit tests can find this node. The default is <see langword="true"/>.
    /// </summary>
    public bool InputEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a box in node-local logical coordinates that limits input hits. <see langword="null"/> applies no limit.
    /// </summary>
    public Box? InputBox { get; set; }

    /// <summary>
    /// Gets or sets a filter that can refuse input at a position after the other checks pass. <see langword="null"/> refuses nothing.
    /// </summary>
    public ISceneInputFilter? InputFilter { get; set; }

    /// <summary>
    /// Gets or sets the fence that the renderer must wait on before it reads the buffer, or -1 for none.
    /// The getter reads the fence of <see cref="FenceSurface"/> while <see cref="FenceFromSurface"/> is set. The setter clears that link.
    /// The node does not own the descriptor.
    /// </summary>
    public int AcquireFenceFd
    {
        get => FenceFromSurface ? FenceSurface?.AcquireFenceFd ?? -1 : _acquireFenceFd;
        set
        {
            FenceFromSurface = false;
            _acquireFenceFd = value;
        }
    }

    /// <summary>
    /// Gets the surface whose acquire fence the node uses, as set by <see cref="FollowFenceOf"/>.
    /// </summary>
    public Surface? FenceSurface { get; private set; }

    /// <summary>
    /// Gets a value indicating whether <see cref="AcquireFenceFd"/> reads the fence of <see cref="FenceSurface"/>.
    /// </summary>
    public bool FenceFromSurface { get; private set; }

    /// <summary>
    /// Makes <see cref="AcquireFenceFd"/> read the current acquire fence of a surface on each frame. It drops a fence set before.
    /// </summary>
    /// <param name="surface">The surface to follow, or <see langword="null"/> to use no fence.</param>
    public void FollowFenceOf(Surface? surface)
    {
        FenceSurface = surface;
        FenceFromSurface = surface is not null;
        _acquireFenceFd = -1;
    }

    private int _acquireFenceFd = -1;

    /// <summary>
    /// Gets or sets the part of the buffer to show, in buffer pixels. The default empty box shows the whole buffer.
    /// A change damages the node.
    /// </summary>
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

    /// <summary>
    /// Gets a value indicating whether the node keeps its destination size with no buffer, so that it can carry only a backdrop effect.
    /// </summary>
    public bool BackdropOnly { get; init; }

    /// <summary>
    /// Gets or sets the width in logical pixels that the buffer is scaled to. 0 uses the buffer width.
    /// </summary>
    public int DestinationWidth
    {
        get => _destinationWidth;
        set => ResizeDestination(ref _destinationWidth, value);
    }

    /// <summary>
    /// Gets or sets the height in logical pixels that the buffer is scaled to. 0 uses the buffer height.
    /// </summary>
    public int DestinationHeight
    {
        get => _destinationHeight;
        set => ResizeDestination(ref _destinationHeight, value);
    }

    /// <summary>
    /// Places the node so that its buffer covers an exact box of device pixels at one output scale.
    /// It sets the position, the destination size, <see cref="DevicePixels"/>, <see cref="DeviceNudge"/> and <see cref="DeviceScale"/>.
    /// </summary>
    /// <param name="originX">The horizontal logical offset to add to the position.</param>
    /// <param name="originY">The vertical logical offset to add to the position.</param>
    /// <param name="scale">The output scale that the device box is for.</param>
    /// <param name="device">The box in device pixels that the buffer must cover.</param>
    public void PlaceDevice(int originX, int originY, double scale, in Box device)
    {
        var (x, nudgeX) = DevicePlacement.FromDevice(device.X, scale);
        var (y, nudgeY) = DevicePlacement.FromDevice(device.Y, scale);
        SetPosition(originX + x, originY + y);
        DestinationWidth = DevicePlacement.ToLogicalSpan(device.Width + nudgeX, scale) + 1;
        DestinationHeight = DevicePlacement.ToLogicalSpan(device.Height + nudgeY, scale) + 1;
        DeviceNudge = (nudgeX, nudgeY);
        DevicePixels = (device.Width, device.Height);
        DeviceScale = scale;
    }

    /// <summary>
    /// Gets or sets the exact size in device pixels that the buffer covers at <see cref="DeviceScale"/>.
    /// <see langword="null"/> means that the size comes from the logical box.
    /// </summary>
    public (int Width, int Height)? DevicePixels
    {
        get => _device?.Pixels;
        set
        {
            if (Nullable.Equals(_device?.Pixels, value))
            {
                return;
            }

            DamageSubtree();
            (_device ??= new SceneBufferDevice()).Pixels = value;
            DamageSubtree();
        }
    }

    /// <summary>
    /// Gets or sets the output scale at which <see cref="DevicePixels"/> and <see cref="DeviceNudge"/> apply. The default is 1.
    /// </summary>
    public double DeviceScale
    {
        get => _device?.Scale ?? 1.0;
        set
        {
            if (DeviceScale == value)
            {
                return;
            }

            DamageSubtree();
            (_device ??= new SceneBufferDevice()).Scale = value;
            DamageSubtree();
        }
    }

    /// <summary>
    /// Gets or sets the offset in device pixels from the rounded logical position to the exact device position.
    /// </summary>
    public (int X, int Y) DeviceNudge
    {
        get => _device?.Nudge ?? default;
        set
        {
            if (DeviceNudge == value)
            {
                return;
            }

            DamageSubtree();
            (_device ??= new SceneBufferDevice()).Nudge = value;
            DamageSubtree();
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether every pixel of the buffer is opaque.
    /// An opaque node can hide the nodes below it and can skip blending.
    /// </summary>
    public bool IsOpaque { get; set; }

    private Box? _visibleBox;

    /// <summary>
    /// Gets or sets a box in node-local logical coordinates that clips what the node draws. <see langword="null"/> applies no clip.
    /// </summary>
    public Box? VisibleBox
    {
        get => _visibleBox;
        set
        {
            if (Nullable.Equals(_visibleBox, value))
            {
                return;
            }

            DamageSubtree();
            _visibleBox = value;
            DamageSubtree();
        }
    }

    private PixmanRegion32? _opaqueRegion;

    internal PixmanRegion32? OpaqueRegion => _opaqueRegion is { IsEmpty: false } region ? region : null;

    /// <summary>
    /// Copies the region of the buffer that is opaque, in node-local logical coordinates. A change damages the node.
    /// </summary>
    /// <param name="region">The opaque region, or <see langword="null"/> or an empty region to clear it. The node does not keep the instance.</param>
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

    private ImageDescription? _colorDescription;

    /// <summary>
    /// Gets or sets the color space of the buffer content. <see langword="null"/> means the default SDR description.
    /// A change tells the outputs to make a color lookup table for it.
    /// </summary>
    public ImageDescription? ColorDescription
    {
        get => _colorDescription;
        set
        {
            if (ReferenceEquals(_colorDescription, value))
            {
                return;
            }

            _colorDescription = value;
            RootOwner()?.NotifyColorDescription(this);
            DamageSubtree();
        }
    }

    private IPixelShader? _textureShader;

    /// <summary>
    /// Gets or sets a pixel shader that the renderer runs on the texture when it draws this node. <see langword="null"/> uses no shader.
    /// A node with a shader never goes to a plane.
    /// </summary>
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

    /// <summary>
    /// Gets the effect that the renderer applies to the content behind this node, or <see langword="null"/> when there is none.
    /// </summary>
    public IBackdropEffect? BackdropEffect => _backdropEffect;

    /// <summary>
    /// Gets the key that the renderer uses to keep backdrop state between frames, as given to <see cref="SetBackdropEffect"/>.
    /// </summary>
    public object? BackdropKey { get; private set; }

    internal PixmanRegion32? BackdropRegion => _backdropRegion;

    internal bool HasActiveBackdrop => _backdropEffect is not null && _backdropRegion is { IsEmpty: false };

    /// <summary>
    /// Sets the effect that the renderer applies to the content behind this node, and the region where it applies.
    /// </summary>
    /// <param name="effect">The effect, or <see langword="null"/> to remove it.</param>
    /// <param name="region">The region in node-local logical coordinates. An empty or <see langword="null"/> region turns the effect off.</param>
    /// <param name="key">An identity for the effect state between frames, or <see langword="null"/>.</param>
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

    internal void ApplySurfaceBackdrop(ISurfaceBackdrop? backdrop, Surface surface)
    {
        var ours = ReferenceEquals(BackdropKey, this) && _backdropEffect is not null;
        var wasActive = ours && HasActiveBackdrop;
        if (backdrop?.Effect is { } effect)
        {
            _backdropRegion ??= new PixmanRegion32();
            if (backdrop.RegionOf(surface, _backdropRegion) && !_backdropRegion.IsEmpty)
            {
                _backdropEffect = effect;
                BackdropKey = this;
                DamageSubtree();
                return;
            }

            _backdropRegion.Clear();
        }

        if (!ours)
        {
            return;
        }

        _backdropEffect = null;
        BackdropKey = null;
        _backdropRegion?.Clear();
        if (wasActive)
        {
            DamageSubtree();
        }
    }

    /// <summary>
    /// Tests whether the node takes input at a position.
    /// It checks <see cref="InputEnabled"/>, the content size, <see cref="InputBox"/>, <see cref="InputFilter"/> and the surface input region.
    /// </summary>
    /// <param name="x">The horizontal position in node-local logical coordinates.</param>
    /// <param name="y">The vertical position in node-local logical coordinates.</param>
    /// <returns><see langword="true"/> when every check passes, otherwise <see langword="false"/>.</returns>
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

        if (InputFilter is { } filter && !filter.AcceptsInputAt(this, x, y))
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

    /// <summary>
    /// Shows a new buffer. The node locks the new buffer and unlocks the previous one.
    /// It damages the node when the size changes. The caller reports content damage with <see cref="NotifyContentChanged(PixmanRegion32)"/>.
    /// </summary>
    /// <param name="buffer">The buffer to show, or <see langword="null"/> to show nothing.</param>
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
        ContentVersion++;
        if (sizeChanges && buffer is not null)
        {
            DamageSubtree();
        }
    }

    /// <summary>
    /// Tells the node that all of the buffer content changed. It refreshes the texture and damages the whole node.
    /// </summary>
    public void NotifyContentChanged()
    {
        ContentVersion++;
        _conversion?.Refresh();
        (CurrentTexture as IRefreshableTexture)?.MarkDirty();
        DamageSubtree();
    }

    /// <summary>
    /// Gets or sets a value indicating whether buffer damage maps to node damage even when the buffer is scaled or cropped.
    /// When it is <see langword="false"/>, damage on a scaled buffer damages the whole node.
    /// </summary>
    public bool PreciseDamage { get; set; }

    /// <summary>
    /// Tells the node that part of the buffer content changed. An empty region does nothing.
    /// </summary>
    /// <param name="damage">The changed area in buffer pixels.</param>
    public void NotifyContentChanged(PixmanRegion32 damage)
    {
        var none = default(DamageRects);
        NotifyContentChanged(damage, in none);
    }

    /// <summary>
    /// Tells the node that part of the buffer content changed, with the separate rectangles so the texture uploads only those.
    /// </summary>
    /// <param name="damage">The changed area in buffer pixels. An empty region does nothing.</param>
    /// <param name="rects">The separate damage rectangles, or an empty set to use the extents of <paramref name="damage"/>.</param>
    public void NotifyContentChanged(PixmanRegion32 damage, in DamageRects rects)
    {
        _conversion?.Refresh();
        if (damage.IsEmpty)
        {
            return;
        }

        ContentVersion++;

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

    /// <summary>
    /// Gets the size in logical pixels that the node draws. It is the destination size when set, otherwise the buffer size.
    /// With no buffer, it is 0 by 0, or the destination size for a <see cref="BackdropOnly"/> node.
    /// </summary>
    protected override (int Width, int Height) ContentSize
    {
        get
        {
            if (_lock.Buffer is not { } buffer)
            {
                return BackdropOnly ? (_destinationWidth, _destinationHeight) : (0, 0);
            }

            return (
                _destinationWidth > 0 ? _destinationWidth : buffer.Width,
                _destinationHeight > 0 ? _destinationHeight : buffer.Height);
        }
    }

    internal (int Width, int Height) Size => ContentSize;

    /// <summary>
    /// Gets the size in logical pixels that the node draws, the same value as the content size of the node.
    /// </summary>
    public (int Width, int Height) DisplaySize => ContentSize;

    internal bool IsExactAt(double scale) => _device is { Pixels: not null } device && device.Scale == scale;

    internal Box DeviceBox(int x, int y, double scale)
    {
        var (width, height) = _device?.Pixels ?? default;
        var nudge = _device?.Nudge ?? default;
        return new Box((int)Math.Round(x * scale) + nudge.X, (int)Math.Round(y * scale) + nudge.Y, width, height);
    }

    /// <summary>
    /// Gets a counter that grows each time the buffer or its content changes. A cache can compare it to find stale data.
    /// </summary>
    public int ContentVersion { get; private set; }

    /// <summary>
    /// Gets the texture of the buffer for a renderer, and imports it on first use.
    /// </summary>
    /// <param name="renderer">The renderer that draws the texture.</param>
    /// <returns>The texture, or <see langword="null"/> when there is no buffer or the renderer cannot import it. The node or the scene owns it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="renderer"/> is <see langword="null"/>.</exception>
    public ITexture? TextureFor(IRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        return GetTexture(renderer);
    }

    internal ITexture? GetTexture(IRenderer renderer)
    {
        if (_lock.Buffer is not { } buffer)
        {
            return null;
        }

        if (OwnerIfAttached() is not { } scene)
        {
            if (_texture is null && !_importFailed)
            {
                Basin.Diagnostics.AllocationScope.Pause();
                _texture = renderer.ImportTexture(buffer);
                Basin.Diagnostics.AllocationScope.Resume();
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

        Basin.Diagnostics.AllocationScope.Pause();
        try
        {
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
        finally
        {
            Basin.Diagnostics.AllocationScope.Resume();
        }
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
        _texture ?? (_lock.Buffer is { } buffer ? OwnerIfAttached()?.PeekTexture(buffer) : null);

    /// <summary>
    /// Releases the texture, the buffer lock and the regions of the node.
    /// </summary>
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
