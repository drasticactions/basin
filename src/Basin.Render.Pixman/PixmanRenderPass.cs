using Basin.Diagnostics;
using Pixman;

namespace Basin.Render.Pixman;

internal sealed class PixmanRenderPass : IRenderPass
{
    private readonly PixmanRegion32 _fullClip = new();
    private IBuffer? _target;
    private PixmanImage? _image;
    private nint _targetData;
    private int _targetStride;
    private ColorLut3D? _solid;
    private MeshVertex[] _solidVertices = [];

    internal void Begin(IBuffer target, PixmanImage image, nint targetData, int targetStride, ColorLut3D? solid = null)
    {
        if (_target is not null)
        {
            throw new InvalidOperationException("The previous render pass was not submitted.");
        }

        _target = target;
        _image = image;
        _targetData = targetData;
        _targetStride = targetStride;
        _solid = solid;
    }

    private RenderColor Solid(in RenderColor color) => _solid is { } lut ? lut.ApplyPremultiplied(color) : color;

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
        _image!.Fill(color.A >= 1f ? PixmanOp.Src : PixmanOp.Over, ToPixmanColor(Solid(color)), boxes);
        ResetClip(clip);
    }

    public void Clear(in RenderColor color)
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        Span<PixmanBox32> boxes = [new PixmanBox32(0, 0, _target.Width, _target.Height)];
        _image!.Fill(PixmanOp.Src, ToPixmanColor(Solid(color)), boxes);
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
        var effects = !options.Corners.IsEmpty || options.Brightness != 1f || options.Saturation != 1f;

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
                TransformIntoScratch(image, lut.Lut, bounds, scaled && options.Transform.IsIdentity);
                source = _scratchImage!;
                sourceX = src.X - bounds.X;
                sourceY = src.Y - bounds.Y;
            }

            if (effects)
            {
                RenderEffects(source, sourceX, sourceY, src, scaled, options);
                source = _effectImage!;
                sourceX = 0;
                sourceY = 0;
                src = new FBox(0, 0, options.DstBox.Width, options.DstBox.Height);
                scaled = false;
            }

            if (!options.Transform.IsIdentity)
            {
                if (!effects && options.Lut is not PixmanColorLut &&
                    (src.X > 0 || src.Y > 0 || src.Width < texture.Width || src.Height < texture.Height))
                {
                    var bounds = src.RoundedOut();
                    CopyIntoScratch(image, bounds);
                    source = _scratchImage!;
                    sourceX = src.X - bounds.X;
                    sourceY = src.Y - bounds.Y;
                }

                if (ReferenceEquals(source, _scratchImage))
                {
                    var used = src.RoundedOut();
                    ClearStaleScratch(used.Width, used.Height);
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
                source.SetFilter(options.Filter != TextureFilter.Linear ? PixmanFilter.Nearest : PixmanFilter.Bilinear);
                source.SetRepeat(PixmanRepeat.Pad);
            }

            ApplyClip(options.Clip);
            var mask = options.Alpha < 1f ? AlphaMask(options.Alpha) : null;
            _image!.Composite(
                options.Opaque && options.Alpha >= 1f && options.Corners.IsEmpty ? PixmanOp.Src : PixmanOp.Over,
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

    private byte[]? _effect;
    private System.Runtime.InteropServices.GCHandle _effectPin;
    private PixmanImage? _effectImage;
    private int _effectWidth;
    private int _effectHeight;

    private unsafe void RenderEffects(PixmanImage source, double sourceX, double sourceY, in FBox src, bool scaled, in TextureRenderOptions options)
    {
        var width = options.DstBox.Width;
        var height = options.DstBox.Height;
        EnsureEffect(width + 1, height + 1);
        if (scaled)
        {
            var translate = PixmanTransform.CreateTranslate(sourceX, sourceY);
            var scale = PixmanTransform.CreateScale(src.Width / width, src.Height / height);
            var transform = PixmanTransform.Multiply(in translate, in scale);
            source.SetTransform(in transform);
            source.SetFilter(options.Filter != TextureFilter.Linear ? PixmanFilter.Nearest : PixmanFilter.Bilinear);
            source.SetRepeat(PixmanRepeat.Pad);
        }

        _effectImage!.Composite(
            PixmanOp.Src, source, null, scaled ? 0 : (int)sourceX, scaled ? 0 : (int)sourceY, 0, 0, 0, 0, width, height);
        if (scaled)
        {
            source.ClearTransform();
            source.SetFilter(PixmanFilter.Nearest);
            source.SetRepeat(PixmanRepeat.None);
        }

        var pixels = (uint*)_effectPin.AddrOfPinnedObject();
        var stride = _effectWidth;
        if (options.Brightness != 1f || options.Saturation != 1f)
        {
            Modulate(pixels, stride, width, height, options.Brightness, options.Saturation);
        }

        if (!options.Corners.IsEmpty)
        {
            ClipCorners(pixels, stride, width, height, options.Corners);
        }

        new Span<uint>(pixels + (height * stride), width + 1).Clear();
        for (var y = 0; y < height; y++)
        {
            pixels[(y * stride) + width] = 0;
        }
    }

    private static unsafe void Modulate(uint* pixels, int stride, int width, int height, float brightness, float saturation)
    {
        for (var y = 0; y < height; y++)
        {
            var row = pixels + (y * stride);
            for (var x = 0; x < width; x++)
            {
                var pixel = row[x];
                var alpha = pixel >> 24;
                if (alpha == 0)
                {
                    continue;
                }

                var scale = 1f / alpha;
                var red = ((pixel >> 16) & 0xFF) * scale;
                var green = ((pixel >> 8) & 0xFF) * scale;
                var blue = (pixel & 0xFF) * scale;
                var luma = (0.2126f * red) + (0.7152f * green) + (0.0722f * blue);
                red = (luma + ((red - luma) * saturation)) * brightness;
                green = (luma + ((green - luma) * saturation)) * brightness;
                blue = (luma + ((blue - luma) * saturation)) * brightness;
                row[x] = (alpha << 24)
                    | (Channel(red, alpha) << 16)
                    | (Channel(green, alpha) << 8)
                    | Channel(blue, alpha);
            }
        }
    }

    private static uint Channel(float straight, uint alpha) => (uint)Math.Clamp((int)((straight * alpha) + 0.5f), 0, (int)alpha);

    private static unsafe void ClipCorners(uint* pixels, int stride, int width, int height, in CornerClip corners)
    {
        var outer = corners.OuterWidth > 0 && corners.OuterHeight > 0;
        var left = outer ? -corners.OffsetX : 0;
        var top = outer ? -corners.OffsetY : 0;
        var right = outer ? left + corners.OuterWidth : width;
        var bottom = outer ? top + corners.OuterHeight : height;
        var radius = Math.Min(corners.Radius, Math.Min(right - left, bottom - top) / 2);
        var centerX = (left + right) / 2;
        var centerY = (top + bottom) / 2;
        var halfX = (right - left) / 2;
        var halfY = (bottom - top) / 2;
        for (var y = 0; y < height; y++)
        {
            var py = y + 0.5;
            var inBand = py < top + radius || py > bottom - radius;
            if (!inBand)
            {
                continue;
            }

            var row = pixels + (y * stride);
            for (var x = 0; x < width; x++)
            {
                var px = x + 0.5;
                var dx = Math.Max(Math.Abs(px - centerX) - (halfX - radius), 0);
                var dy = Math.Max(Math.Abs(py - centerY) - (halfY - radius), 0);
                var distance = Math.Sqrt((dx * dx) + (dy * dy)) - radius;
                var coverage = Math.Clamp(0.5 - distance, 0, 1);
                if (coverage >= 1)
                {
                    continue;
                }

                var pixel = row[x];
                var a = (uint)(((pixel >> 24) * coverage) + 0.5);
                var cr = (uint)((((pixel >> 16) & 0xFF) * coverage) + 0.5);
                var cg = (uint)((((pixel >> 8) & 0xFF) * coverage) + 0.5);
                var cb = (uint)(((pixel & 0xFF) * coverage) + 0.5);
                row[x] = (a << 24) | (cr << 16) | (cg << 8) | cb;
            }
        }
    }

    private void EnsureEffect(int width, int height)
    {
        if (_effectImage is not null && width <= _effectWidth && height <= _effectHeight)
        {
            return;
        }

        var effectWidth = Math.Max(width, _effectWidth);
        var effectHeight = Math.Max(height, _effectHeight);
        DropEffect();
        _effect = new byte[effectWidth * effectHeight * 4];
        _effectPin = System.Runtime.InteropServices.GCHandle.Alloc(
            _effect, System.Runtime.InteropServices.GCHandleType.Pinned);
        _effectImage = PixmanImage.CreateBits(
            PixmanFormat.A8R8G8B8, effectWidth, effectHeight, _effectPin.AddrOfPinnedObject(), effectWidth * 4);
        _effectWidth = effectWidth;
        _effectHeight = effectHeight;
    }

    private void DropEffect()
    {
        _effectImage?.Dispose();
        _effectImage = null;
        _effectWidth = 0;
        _effectHeight = 0;
        if (_effectPin.IsAllocated)
        {
            _effectPin.Free();
        }

        _effect = null;
    }

    private byte[]? _alphaMaskBits;
    private System.Runtime.InteropServices.GCHandle _alphaMaskPin;
    private PixmanImage? _alphaMask;

    private PixmanImage AlphaMask(float alpha)
    {
        if (_alphaMask is null)
        {
            _alphaMaskBits = new byte[4];
            _alphaMaskPin = System.Runtime.InteropServices.GCHandle.Alloc(
                _alphaMaskBits, System.Runtime.InteropServices.GCHandleType.Pinned);
            _alphaMask = PixmanImage.CreateBits(PixmanFormat.A8, 1, 1, _alphaMaskPin.AddrOfPinnedObject(), 4);
            _alphaMask.SetRepeat(PixmanRepeat.Normal);
        }

        _alphaMaskBits![0] = (byte)((ushort)(alpha * ushort.MaxValue) >> 8);
        return _alphaMask;
    }

    private void DropAlphaMask()
    {
        _alphaMask?.Dispose();
        _alphaMask = null;
        if (_alphaMaskPin.IsAllocated)
        {
            _alphaMaskPin.Free();
        }

        _alphaMaskBits = null;
    }

    private byte[]? _scratch;
    private System.Runtime.InteropServices.GCHandle _scratchPin;
    private PixmanImage? _scratchImage;
    private int _scratchWidth;
    private int _scratchHeight;
    private int _scratchDirtyWidth;
    private int _scratchDirtyHeight;

    private unsafe void TransformIntoScratch(PixmanImage source, ColorLut3D lut, Box src, bool padEdge)
    {
        EnsureScratch(src.Width, src.Height);
        _scratchImage!.Composite(PixmanOp.Src, source, null, src.X, src.Y, 0, 0, 0, 0, src.Width, src.Height);

        var pixels = (uint*)_scratchPin.AddrOfPinnedObject();
        for (var y = 0; y < src.Height; y++)
        {
            var row = pixels + (y * _scratchWidth);
            for (var x = 0; x < src.Width; x++)
            {
                var value = row[x];
                var alpha = value >> 24;
                if (alpha == 0)
                {
                    continue;
                }

                var fa = alpha / 255f;
                var (r, g, b) = lut.Sample(
                    ColorLut3D.Shape(((value >> 16) & 0xFF) / 255f / fa, lut.ShaperNits),
                    ColorLut3D.Shape(((value >> 8) & 0xFF) / 255f / fa, lut.ShaperNits),
                    ColorLut3D.Shape((value & 0xFF) / 255f / fa, lut.ShaperNits));
                row[x] = (alpha << 24)
                    | ((uint)(Math.Clamp(r, 0f, 1f) * fa * 255f + 0.5f) << 16)
                    | ((uint)(Math.Clamp(g, 0f, 1f) * fa * 255f + 0.5f) << 8)
                    | (uint)(Math.Clamp(b, 0f, 1f) * fa * 255f + 0.5f);
            }
        }

        EdgeScratch(src.Width, src.Height, padEdge);
    }

    private unsafe void ClearStaleScratch(int width, int height)
    {
        var pixels = (uint*)_scratchPin.AddrOfPinnedObject();
        var stride = _scratchWidth;
        var edgeWidth = Math.Min(width + 1, stride);
        var edgeHeight = Math.Min(height + 1, _scratchHeight);
        if (_scratchDirtyWidth > edgeWidth)
        {
            for (var y = 0; y < Math.Min(edgeHeight, _scratchDirtyHeight); y++)
            {
                new Span<uint>(pixels + (y * stride) + edgeWidth, _scratchDirtyWidth - edgeWidth).Clear();
            }
        }

        for (var y = edgeHeight; y < _scratchDirtyHeight; y++)
        {
            new Span<uint>(pixels + (y * stride), _scratchDirtyWidth).Clear();
        }

        _scratchDirtyWidth = edgeWidth;
        _scratchDirtyHeight = edgeHeight;
    }

    private unsafe void EdgeScratch(int width, int height, bool pad)
    {
        _scratchDirtyWidth = Math.Max(_scratchDirtyWidth, Math.Min(width + 1, _scratchWidth));
        _scratchDirtyHeight = Math.Max(_scratchDirtyHeight, Math.Min(height + 1, _scratchHeight));
        var pixels = (uint*)_scratchPin.AddrOfPinnedObject();
        var stride = _scratchWidth;
        if (width < stride)
        {
            for (var y = 0; y < height; y++)
            {
                pixels[(y * stride) + width] = pad ? pixels[(y * stride) + width - 1] : 0;
            }
        }

        if (height < _scratchHeight)
        {
            var count = Math.Min(width + 1, stride);
            var edge = pixels + (height * stride);
            if (pad)
            {
                Buffer.MemoryCopy(pixels + ((height - 1) * stride), edge, count * 4, count * 4);
            }
            else
            {
                new Span<uint>(edge, count).Clear();
            }
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
            throw new ArgumentException("The vertex count must be a multiple of three.", nameof(vertices));
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
        else if (_solid is { } lut && options.Blend != RenderBlend.Additive)
        {
            if (_solidVertices.Length < vertices.Length)
            {
                _solidVertices = new MeshVertex[vertices.Length];
            }

            for (var i = 0; i < vertices.Length; i++)
            {
                _solidVertices[i] = vertices[i] with { Color = lut.ApplyPremultiplied(vertices[i].Color) };
            }

            vertices = _solidVertices.AsSpan(0, vertices.Length);
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
        EdgeScratch(src.Width, src.Height, pad: false);
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

        var linear = Math.Sqrt(Math.Abs((options.Transform.M11 * options.Transform.M22) - (options.Transform.M12 * options.Transform.M21)));
        var magnified = linear / Math.Max(kx, ky) >= 2;
        source.SetTransform(in sample);
        source.SetFilter(magnified || options.Filter != TextureFilter.Linear ? PixmanFilter.Nearest : PixmanFilter.Bilinear);
        source.SetRepeat(PixmanRepeat.None);
        ApplyClip(options.Clip);
        var mask = options.Alpha < 1f ? AlphaMask(options.Alpha) : null;
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
        if (_scratchImage is not null && width <= _scratchWidth && height <= _scratchHeight)
        {
            return;
        }

        var scratchWidth = Math.Max(width, _scratchWidth);
        var scratchHeight = Math.Max(height, _scratchHeight);
        DropScratch();
        _scratch = new byte[scratchWidth * scratchHeight * 4];
        _scratchPin = System.Runtime.InteropServices.GCHandle.Alloc(
            _scratch, System.Runtime.InteropServices.GCHandleType.Pinned);
        _scratchImage = PixmanImage.CreateBits(
            PixmanFormat.A8R8G8B8, scratchWidth, scratchHeight, _scratchPin.AddrOfPinnedObject(), scratchWidth * 4);
        _scratchWidth = scratchWidth;
        _scratchHeight = scratchHeight;
    }

    private void DropScratch()
    {
        _scratchImage?.Dispose();
        _scratchImage = null;
        _scratchWidth = 0;
        _scratchHeight = 0;
        _scratchDirtyWidth = 0;
        _scratchDirtyHeight = 0;
        if (_scratchPin.IsAllocated)
        {
            _scratchPin.Free();
        }

        _scratch = null;
    }

    private int _scopedSubmits;

    public bool Submit()
    {
        if (_scopedSubmits < 30)
        {
            _scopedSubmits++;
            return SubmitCore();
        }

        AllocationScope.Begin(region: "PixmanSubmit", forgiving: true);
        try
        {
            return SubmitCore();
        }
        finally
        {
            AllocationScope.End();
        }
    }

    private bool SubmitCore()
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        var target = _target;
        _target = null;
        _solid = null;
        _image = null;
        target.EndDataAccess();
        return true;
    }

    internal void DisposeScratch()
    {
        _fullClip.Dispose();
        DropScratch();
        DropEffect();
        DropAlphaMask();
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
