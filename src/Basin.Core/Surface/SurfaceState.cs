using Basin.Diagnostics;
using Pixman;
using Wayland;

namespace Basin;

/// <summary>
/// One side of a surface's double-buffered state: a pending delta, a cached delta of a synchronized subsurface, or the current state. It holds one lock on <see cref="Buffer"/>.
/// </summary>
public sealed class SurfaceState : IDisposable
{
    /// <summary>The fields that this delta sets. On a current state it accumulates every field ever applied.</summary>
    public SurfaceStateFields Committed;

    /// <summary>The attached buffer, or <see langword="null"/>. Use <see cref="SetBuffer"/> to keep the lock count right.</summary>
    public IBuffer? Buffer;

    /// <summary>The horizontal offset of the new content relative to the old, in logical coordinates after the commit converts it.</summary>
    public int OffsetX;

    /// <summary>The vertical offset of the new content relative to the old, in logical coordinates after the commit converts it.</summary>
    public int OffsetY;

    /// <summary>Gets the damage in surface-local coordinates. On a current state it describes one commit only.</summary>
    public PixmanRegion32 SurfaceDamage { get; } = new();

    /// <summary>Gets the damage in buffer pixels. On a current state it describes one commit only.</summary>
    public PixmanRegion32 BufferDamage { get; } = new();

    /// <summary>The surface damage as up to four rectangles, without allocation.</summary>
    public DamageRects SurfaceDamageRects;

    /// <summary>The buffer damage as up to four rectangles, without allocation.</summary>
    public DamageRects BufferDamageRects;

    private PixmanRegion32? _opaque;
    private PixmanRegion32? _input;

    /// <summary>Gets the opaque region in surface-local coordinates. The first read allocates it.</summary>
    public PixmanRegion32 Opaque => _opaque ??= new();

    /// <summary>Gets the input region in surface-local coordinates. The first read allocates it. It is ignored while <see cref="InputIsInfinite"/> is <see langword="true"/>.</summary>
    public PixmanRegion32 Input => _input ??= new();

    /// <summary>Gets a value indicating whether <see cref="Opaque"/> is allocated. When it is <see langword="false"/>, the opaque region is empty.</summary>
    public bool HasOpaque => _opaque is not null;

    /// <summary>Gets a value indicating whether <see cref="Input"/> is allocated.</summary>
    public bool HasInput => _input is not null;

    /// <summary>A value indicating whether the input region covers the whole surface. It is the default.</summary>
    public bool InputIsInfinite = true;

    /// <summary>The buffer transform.</summary>
    public OutputTransform Transform;

    /// <summary>The integer buffer scale.</summary>
    public int Scale = 1;

    /// <summary>The viewport source rectangle in surface-oriented buffer coordinates. A negative width or height means no source rectangle.</summary>
    public double ViewportSourceX, ViewportSourceY, ViewportSourceWidth = -1, ViewportSourceHeight = -1;

    /// <summary>The viewport destination size in surface coordinates. A non-positive value means no destination size.</summary>
    public int ViewportDestinationWidth = -1, ViewportDestinationHeight = -1;

    /// <summary>Gets the delegate and resource callbacks that the next frame settles.</summary>
    public List<FrameCallback> FrameCallbacks { get; } = [];

    /// <summary>Gets the <c>wl_surface.frame</c> callback resources that the next frame sends <c>done</c> to.</summary>
    public List<WlCallbackResource> FrameResources { get; } = [];

    /// <summary>The <c>wl_surface.get_release</c> callback. It fires when a later commit replaces this state's buffer, and it is cancelled when the state is discarded.</summary>
    public FrameCallback? BufferRelease;

    private IDisposable?[]? _extensions;

    /// <summary>
    /// Stores an extension payload in the slot of its type. A payload that was there is disposed.
    /// </summary>
    /// <typeparam name="T">The payload type, which picks the slot.</typeparam>
    /// <param name="payload">The payload, or <see langword="null"/> to empty the slot.</param>
    public void SetExtension<T>(T? payload)
        where T : class, IDisposable
    {
        var slot = SurfaceCommitSlot<T>.Index;
        if (_extensions is null)
        {
            if (payload is null)
            {
                return;
            }

            _extensions = new IDisposable?[SurfaceCommitSlots.Count];
        }
        else if (slot >= _extensions.Length)
        {
            Array.Resize(ref _extensions, SurfaceCommitSlots.Count);
        }

        _extensions[slot]?.Dispose();
        _extensions[slot] = payload;
    }

    /// <summary>Reads an extension payload and leaves it in its slot.</summary>
    /// <typeparam name="T">The payload type.</typeparam>
    /// <returns>The payload, or <see langword="null"/> when the slot is empty.</returns>
    public T? GetExtension<T>()
        where T : class, IDisposable
    {
        var slot = SurfaceCommitSlot<T>.Index;
        return _extensions is { } slots && slot < slots.Length ? (T?)slots[slot] : null;
    }

    /// <summary>Removes an extension payload from its slot. The caller owns the result and must dispose it.</summary>
    /// <typeparam name="T">The payload type.</typeparam>
    /// <returns>The payload, or <see langword="null"/> when the slot is empty.</returns>
    public T? TakeExtension<T>()
        where T : class, IDisposable
    {
        var slot = SurfaceCommitSlot<T>.Index;
        if (_extensions is not { } slots || slot >= slots.Length)
        {
            return null;
        }

        var payload = (T?)slots[slot];
        slots[slot] = null;
        return payload;
    }

    internal void MoveExtensionsTo(SurfaceState target)
    {
        if (_extensions is not { } slots)
        {
            return;
        }

        for (var i = 0; i < slots.Length; i++)
        {
            if (slots[i] is not { } payload)
            {
                continue;
            }

            slots[i] = null;
            target._extensions ??= new IDisposable?[SurfaceCommitSlots.Count];
            if (i >= target._extensions.Length)
            {
                Array.Resize(ref target._extensions, SurfaceCommitSlots.Count);
            }

            target._extensions[i]?.Dispose();
            target._extensions[i] = payload;
        }
    }

    /// <summary>Gets the server scale that this state's sizes were derived with.</summary>
    public double ServerScale { get; internal set; } = 1.0;

    /// <summary>Gets the surface width in logical coordinates, as of the last <see cref="UpdateDerivedSize"/>. It is zero without a buffer.</summary>
    public int Width { get; private set; }

    /// <summary>Gets the surface height in logical coordinates, as of the last <see cref="UpdateDerivedSize"/>. It is zero without a buffer.</summary>
    public int Height { get; private set; }

    /// <summary>
    /// Recomputes <see cref="Width"/> and <see cref="Height"/>. The viewport destination wins, then the viewport source, then the buffer size divided by the scale and transposed by a 90 or 270 degree transform.
    /// </summary>
    public void UpdateDerivedSize()
    {
        if (Buffer is null)
        {
            Width = 0;
            Height = 0;
            return;
        }

        var serverScale = ServerScale;
        if (ViewportDestinationWidth > 0 && ViewportDestinationHeight > 0)
        {
            Width = ServerScaling.ToLogical(ViewportDestinationWidth, serverScale);
            Height = ServerScaling.ToLogical(ViewportDestinationHeight, serverScale);
            return;
        }

        if (ViewportSourceWidth >= 0 && ViewportSourceHeight >= 0)
        {
            Width = (int)Math.Round(ViewportSourceWidth / serverScale);
            Height = (int)Math.Round(ViewportSourceHeight / serverScale);
            return;
        }

        var width = Buffer.Width / Scale;
        var height = Buffer.Height / Scale;
        if (serverScale != 1.0)
        {
            width = ServerScaling.ToLogical(width, serverScale);
            height = ServerScaling.ToLogical(height, serverScale);
        }

        if (Transform is OutputTransform.Rotate90 or OutputTransform.Rotate270
            or OutputTransform.Flipped90 or OutputTransform.Flipped270)
        {
            (width, height) = (height, width);
        }

        Width = width;
        Height = height;
    }

    /// <summary>Replaces the buffer. It locks the new buffer before it unlocks the old one.</summary>
    /// <param name="buffer">The new buffer, or <see langword="null"/>.</param>
    public void SetBuffer(IBuffer? buffer)
    {
        buffer?.Lock();
        Buffer?.Unlock();
        Buffer = buffer;
    }

    /// <summary>
    /// Unlocks the buffer, cancels the release and frame callbacks, disposes each extension payload and frees the regions.
    /// </summary>
    public void Dispose()
    {
        SetBuffer(null);
        BufferRelease?.Cancel();
        BufferRelease = null;
        foreach (var callback in FrameCallbacks)
        {
            callback.Cancel();
        }

        FrameCallbacks.Clear();
        FrameResources.Clear();
        if (_extensions is { } slots)
        {
            for (var i = 0; i < slots.Length; i++)
            {
                slots[i]?.Dispose();
                slots[i] = null;
            }
        }

        SurfaceDamage.Dispose();
        BufferDamage.Dispose();
        _opaque?.Dispose();
        _input?.Dispose();
    }
}
