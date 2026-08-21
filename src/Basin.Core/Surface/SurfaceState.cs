using Basin.Diagnostics;
using Pixman;
using Wayland;

namespace Basin;

public sealed class SurfaceState : IDisposable
{
    public SurfaceStateFields Committed;

    public IBuffer? Buffer;

    public int OffsetX;

    public int OffsetY;

    public PixmanRegion32 SurfaceDamage { get; } = new();

    public PixmanRegion32 BufferDamage { get; } = new();

    public PixmanRegion32 Opaque { get; } = new();

    public PixmanRegion32 Input { get; } = new();

    public bool InputIsInfinite = true;

    public OutputTransform Transform;

    public int Scale = 1;

    public double ViewportSourceX, ViewportSourceY, ViewportSourceWidth = -1, ViewportSourceHeight = -1;

    public int ViewportDestinationWidth = -1, ViewportDestinationHeight = -1;

    public List<FrameCallback> FrameCallbacks { get; } = [];

    public List<WlCallbackResource> FrameResources { get; } = [];

    public FrameCallback? BufferRelease;

    private IDisposable?[]? _extensions;

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

    public T? GetExtension<T>()
        where T : class, IDisposable
    {
        var slot = SurfaceCommitSlot<T>.Index;
        return _extensions is { } slots && slot < slots.Length ? (T?)slots[slot] : null;
    }

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

    public int Width { get; private set; }

    public int Height { get; private set; }

    public void UpdateDerivedSize()
    {
        if (Buffer is null)
        {
            Width = 0;
            Height = 0;
            return;
        }

        if (ViewportDestinationWidth > 0 && ViewportDestinationHeight > 0)
        {
            Width = ViewportDestinationWidth;
            Height = ViewportDestinationHeight;
            return;
        }

        if (ViewportSourceWidth >= 0 && ViewportSourceHeight >= 0)
        {
            Width = (int)Math.Round(ViewportSourceWidth);
            Height = (int)Math.Round(ViewportSourceHeight);
            return;
        }

        var width = Buffer.Width / Scale;
        var height = Buffer.Height / Scale;
        if (Transform is OutputTransform.Rotate90 or OutputTransform.Rotate270
            or OutputTransform.Flipped90 or OutputTransform.Flipped270)
        {
            (width, height) = (height, width);
        }

        Width = width;
        Height = height;
    }

    public void SetBuffer(IBuffer? buffer)
    {
        buffer?.Lock();
        Buffer?.Unlock();
        Buffer = buffer;
    }

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
        Opaque.Dispose();
        Input.Dispose();
    }
}
