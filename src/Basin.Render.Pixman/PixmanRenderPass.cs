using Pixman;

namespace Basin.Render.Pixman;

internal sealed class PixmanRenderPass : IRenderPass
{
    private readonly PixmanRegion32 _fullClip = new();
    private IBuffer? _target;
    private PixmanImage? _image;
    private nint _targetData;
    private int _targetStride;

    internal void Begin(IBuffer target, PixmanImage image, nint targetData, int targetStride)
    {
        if (_target is not null)
        {
            throw new InvalidOperationException("The previous render pass was not submitted.");
        }

        _target = target;
        _image = image;
        _targetData = targetData;
        _targetStride = targetStride;
    }

    public void AddRect(in RenderColor color, in Box box, PixmanRegion32? clip = null)
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        var bounded = box.Intersect(new Box(0, 0, _target.Width, _target.Height));
        if (bounded.IsEmpty)
        {
            return;
        }

        Span<PixmanBox32> boxes = [new PixmanBox32(bounded.X, bounded.Y, bounded.Right, bounded.Bottom)];
        ApplyClip(clip);
        _image!.Fill(color.A >= 1f ? PixmanOp.Src : PixmanOp.Over, ToPixmanColor(color), boxes);
        ResetClip(clip);
    }

    public void AddTexture(ITexture texture, in TextureRenderOptions options)
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        var pixmanTexture = (PixmanTexture)texture;
        if (options.DstBox.IsEmpty)
        {
            return;
        }

        var src = options.SrcBox.IsEmpty
            ? new FBox(0, 0, texture.Width, texture.Height)
            : options.SrcBox;
        var scaled = !src.IsPixelAligned
            || src.Width != options.DstBox.Width
            || src.Height != options.DstBox.Height;

        if (!pixmanTexture.Acquire(out var image))
        {
            return;
        }

        try
        {
            var source = image;
            var sourceX = src.X;
            var sourceY = src.Y;
            if (options.Lut is PixmanColorLut lut)
            {
                var bounds = src.RoundedOut();
                TransformIntoScratch(image, lut.Lut, bounds);
                source = _scratchImage!;
                sourceX = src.X - bounds.X;
                sourceY = src.Y - bounds.Y;
            }

            if (!options.Transform.IsIdentity)
            {
                if (options.Lut is not PixmanColorLut &&
                    (src.X > 0 || src.Y > 0 || src.Width < texture.Width || src.Height < texture.Height))
                {
                    var bounds = src.RoundedOut();
                    CopyIntoScratch(image, bounds);
                    source = _scratchImage!;
                    sourceX = src.X - bounds.X;
                    sourceY = src.Y - bounds.Y;
                }

                CompositeTransformed(source, sourceX, sourceY, src, options);
                return;
            }

            if (scaled)
            {
                var translate = PixmanTransform.CreateTranslate(sourceX, sourceY);
                var scale = PixmanTransform.CreateScale(
                    src.Width / options.DstBox.Width,
                    src.Height / options.DstBox.Height);
                var transform = PixmanTransform.Multiply(in translate, in scale);
                source.SetTransform(in transform);
                source.SetFilter(PixmanFilter.Bilinear);
                source.SetRepeat(PixmanRepeat.Pad);
            }

            ApplyClip(options.Clip);
            using var mask = options.Alpha < 1f
                ? PixmanImage.CreateSolidFill(new PixmanColor(0, 0, 0, (ushort)(options.Alpha * ushort.MaxValue)))
                : null;
            _image!.Composite(
                PixmanOp.Over,
                source,
                mask,
                scaled ? 0 : (int)sourceX,
                scaled ? 0 : (int)sourceY,
                0,
                0,
                options.DstBox.X,
                options.DstBox.Y,
                options.DstBox.Width,
                options.DstBox.Height);
            ResetClip(options.Clip);
            if (scaled)
            {
                source.ClearTransform();
                source.SetFilter(PixmanFilter.Nearest);
                source.SetRepeat(PixmanRepeat.None);
            }
        }
        finally
        {
            pixmanTexture.Release();
        }
    }

    private byte[]? _scratch;
    private System.Runtime.InteropServices.GCHandle _scratchPin;
    private PixmanImage? _scratchImage;
    private int _scratchWidth;
    private int _scratchHeight;

    private unsafe void TransformIntoScratch(PixmanImage source, ColorLut3D lut, Box src)
    {
        EnsureScratch(src.Width, src.Height);
        _scratchImage!.Composite(PixmanOp.Src, source, null, src.X, src.Y, 0, 0, 0, 0, src.Width, src.Height);

        var pixels = (uint*)_scratchPin.AddrOfPinnedObject();
        var count = src.Width * src.Height;
        for (var i = 0; i < count; i++)
        {
            var value = pixels[i];
            var alpha = value >> 24;
            if (alpha == 0)
            {
                continue;
            }

            var fa = alpha / 255f;
            var (r, g, b) = lut.Sample(
                ((value >> 16) & 0xFF) / 255f / fa,
                ((value >> 8) & 0xFF) / 255f / fa,
                (value & 0xFF) / 255f / fa);
            pixels[i] = (alpha << 24)
                | ((uint)(Math.Clamp(r, 0f, 1f) * fa * 255f + 0.5f) << 16)
                | ((uint)(Math.Clamp(g, 0f, 1f) * fa * 255f + 0.5f) << 8)
                | (uint)(Math.Clamp(b, 0f, 1f) * fa * 255f + 0.5f);
        }
    }

    public void AddMesh(ITexture? texture, ReadOnlySpan<MeshVertex> vertices, in MeshRenderOptions options)
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        if (vertices.Length == 0)
        {
            return;
        }

        if (vertices.Length % 3 != 0)
        {
            throw new ArgumentException("vertices must be a whole number of triangles", nameof(vertices));
        }

        nint sourceData = 0;
        var sourceStride = 0;
        var opaque = false;
        PixmanTexture? pixmanTexture = null;
        var sourceWidth = 0;
        var sourceHeight = 0;
        if (texture is not null)
        {
            pixmanTexture = (PixmanTexture)texture;
            if (!pixmanTexture.AcquireData(out sourceData, out sourceStride, out opaque))
            {
                return;
            }

            sourceWidth = texture.Width;
            sourceHeight = texture.Height;
        }

        try
        {
            PixmanMeshRasterizer.Rasterize(
                _targetData,
                _targetStride,
                _target.Width,
                _target.Height,
                sourceData,
                sourceStride,
                sourceWidth,
                sourceHeight,
                opaque,
                texture is not null,
                vertices,
                options.Blend == RenderBlend.Additive,
                options.Clip);
        }
        finally
        {
            pixmanTexture?.Release();
        }
    }

    private void CopyIntoScratch(PixmanImage source, Box src)
    {
        EnsureScratch(src.Width, src.Height);
        _scratchImage!.Composite(PixmanOp.Src, source, null, src.X, src.Y, 0, 0, 0, 0, src.Width, src.Height);
    }

    private void CompositeTransformed(
        PixmanImage source, double sourceX, double sourceY, in FBox src, in TextureRenderOptions options)
    {
        if (!options.Transform.TryInvert(out var inverse) ||
            !options.Transform.TryMapBounds(options.DstBox, out var hull))
        {
            return;
        }

        var bounded = hull.Intersect(new Box(0, 0, _target!.Width, _target.Height));
        if (bounded.IsEmpty)
        {
            return;
        }

        var kx = src.Width / options.DstBox.Width;
        var ky = src.Height / options.DstBox.Height;
        var toSource = RenderTransform.Multiply(
            new RenderTransform(
                kx, 0, sourceX - (options.DstBox.X * kx),
                0, ky, sourceY - (options.DstBox.Y * ky),
                0, 0, 1),
            RenderTransform.Multiply(inverse, RenderTransform.Translation(bounded.X, bounded.Y)));

        var ft = PixmanFTransform.Identity;
        ft[0, 0] = toSource.M11;
        ft[0, 1] = toSource.M12;
        ft[0, 2] = toSource.M13;
        ft[1, 0] = toSource.M21;
        ft[1, 1] = toSource.M22;
        ft[1, 2] = toSource.M23;
        ft[2, 0] = toSource.M31;
        ft[2, 1] = toSource.M32;
        ft[2, 2] = toSource.M33;
        PixmanTransform sample;
        try
        {
            sample = PixmanTransform.FromFTransform(in ft);
        }
        catch (PixmanException)
        {
            return;
        }

        source.SetTransform(in sample);
        source.SetFilter(PixmanFilter.Bilinear);
        source.SetRepeat(PixmanRepeat.None);
        ApplyClip(options.Clip);
        using var mask = options.Alpha < 1f
            ? PixmanImage.CreateSolidFill(new PixmanColor(0, 0, 0, (ushort)(options.Alpha * ushort.MaxValue)))
            : null;
        _image!.Composite(
            PixmanOp.Over,
            source,
            mask,
            0,
            0,
            0,
            0,
            bounded.X,
            bounded.Y,
            bounded.Width,
            bounded.Height);
        ResetClip(options.Clip);
        source.ClearTransform();
        source.SetFilter(PixmanFilter.Nearest);
    }

    private void EnsureScratch(int width, int height)
    {
        if (_scratch is null || _scratch.Length < width * height * 4)
        {
            DropScratch();
            _scratch = new byte[width * height * 4];
            _scratchPin = System.Runtime.InteropServices.GCHandle.Alloc(
                _scratch, System.Runtime.InteropServices.GCHandleType.Pinned);
        }

        if (_scratchImage is null || _scratchWidth != width || _scratchHeight != height)
        {
            _scratchImage?.Dispose();
            _scratchImage = PixmanImage.CreateBits(
                PixmanFormat.A8R8G8B8, width, height, _scratchPin.AddrOfPinnedObject(), width * 4);
            _scratchWidth = width;
            _scratchHeight = height;
        }
    }

    private void DropScratch()
    {
        _scratchImage?.Dispose();
        _scratchImage = null;
        _scratchWidth = 0;
        _scratchHeight = 0;
        if (_scratchPin.IsAllocated)
        {
            _scratchPin.Free();
        }

        _scratch = null;
    }

    public bool Submit()
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        var target = _target;
        _target = null;
        _image = null;
        target.EndDataAccess();
        return true;
    }

    internal void DisposeScratch()
    {
        _fullClip.Dispose();
        DropScratch();
    }

    private void ApplyClip(PixmanRegion32? clip)
    {
        if (clip is not null)
        {
            _image!.SetClipRegion(clip);
        }
    }

    private void ResetClip(PixmanRegion32? clip)
    {
        if (clip is not null)
        {
            _fullClip.Reset(new PixmanBox32(0, 0, _target!.Width, _target.Height));
            _image!.SetClipRegion(_fullClip);
        }
    }

    private static PixmanColor ToPixmanColor(in RenderColor color) => new(
        (ushort)(Math.Clamp(color.R, 0f, 1f) * ushort.MaxValue),
        (ushort)(Math.Clamp(color.G, 0f, 1f) * ushort.MaxValue),
        (ushort)(Math.Clamp(color.B, 0f, 1f) * ushort.MaxValue),
        (ushort)(Math.Clamp(color.A, 0f, 1f) * ushort.MaxValue));
}
