using Basin.Diagnostics;
using NImpeller;
using Pixman;

namespace Basin.Render.Impeller;

internal sealed unsafe class ImpellerGlRenderPass : IRenderPass
{
    private readonly ImpellerGlRenderer _renderer;
    private IBuffer? _target;
    private ImpellerGlRenderer.TargetEntry? _entry;
    private IntPtr _builder;
    private int _signalFenceFd = -1;

    private readonly List<ImpellerGlDmabufTexture> _sampled = [];

    internal ImpellerGlRenderPass(ImpellerGlRenderer renderer)
    {
        _renderer = renderer;
    }

    internal void Begin(IBuffer target, ImpellerGlRenderer.TargetEntry entry, int signalFenceFd)
    {
        if (_target is not null)
        {
            throw new InvalidOperationException("The previous render pass was not submitted.");
        }

        _target = target;
        _entry = entry;
        _signalFenceFd = signalFenceFd;
        _builder = UnsafeNativeMethods.ImpellerDisplayListBuilderNewRaw(null);
        BasinCounters.Track();

        UnsafeNativeMethods.ImpellerDisplayListBuilderTranslateRaw(_builder, 0f, target.Height);
        UnsafeNativeMethods.ImpellerDisplayListBuilderScaleRaw(_builder, 1f, -1f);

        var paint = _renderer.TexturePaint;
        UnsafeNativeMethods.ImpellerPaintSetBlendModeRaw(paint, ImpellerBlendMode.kImpellerBlendModeSource);
        var opaque = new ImpellerColor
        {
            Red = 1f,
            Green = 1f,
            Blue = 1f,
            Alpha = 1f,
            Color_space = ImpellerColorSpace.kImpellerColorSpaceSRGB,
        };
        UnsafeNativeMethods.ImpellerPaintSetColorRaw(paint, &opaque);
        var full = new ImpellerRect { X = 0, Y = 0, Width = target.Width, Height = target.Height };
        UnsafeNativeMethods.ImpellerDisplayListBuilderDrawTextureRectRaw(
            _builder, entry.Snapshot, &full, &full,
            ImpellerTextureSampling.kImpellerTextureSamplingNearestNeighbor, paint);
    }

    public void AddRect(in RenderColor color, in Box box, PixmanRegion32? clip = null)
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        if (box.IsEmpty)
        {
            return;
        }

        var paint = _renderer.RectPaint;
        UnsafeNativeMethods.ImpellerPaintSetBlendModeRaw(paint, color.A >= 1f
            ? ImpellerBlendMode.kImpellerBlendModeSource
            : ImpellerBlendMode.kImpellerBlendModeSourceOver);
        SetPaintColor(paint, color);
        var rect = new ImpellerRect { X = box.X, Y = box.Y, Width = box.Width, Height = box.Height };
        if (clip is null)
        {
            UnsafeNativeMethods.ImpellerDisplayListBuilderDrawRectRaw(_builder, &rect, paint);
            return;
        }

        foreach (var band in RegionRects.Of(clip))
        {
            var clipRect = new ImpellerRect
            {
                X = band.X1,
                Y = band.Y1,
                Width = band.X2 - band.X1,
                Height = band.Y2 - band.Y1,
            };
            UnsafeNativeMethods.ImpellerDisplayListBuilderSaveRaw(_builder);
            UnsafeNativeMethods.ImpellerDisplayListBuilderClipRectRaw(
                _builder, &clipRect, ImpellerClipOperation.kImpellerClipOperationIntersect);
            UnsafeNativeMethods.ImpellerDisplayListBuilderDrawRectRaw(_builder, &rect, paint);
            UnsafeNativeMethods.ImpellerDisplayListBuilderRestoreRaw(_builder);
        }
    }

    public void AddTexture(ITexture texture, in TextureRenderOptions options)
    {
        ObjectDisposedException.ThrowIf(_target is null, this);
        if (options.DstBox.IsEmpty || (!options.Transform.IsIdentity && !options.Transform.TryInvert(out _)))
        {
            return;
        }

        var impellerTexture = (IImpellerGlTexture)texture;
        if (!impellerTexture.Acquire(out var raw))
        {
            return;
        }

        if (impellerTexture is ImpellerGlDmabufTexture { SampledThisPass: false } dmabuf)
        {
            dmabuf.SampledThisPass = true;
            _sampled.Add(dmabuf);
        }

        var src = options.SrcBox.IsEmpty
            ? new FBox(0, 0, texture.Width, texture.Height)
            : options.SrcBox;
        var srcRect = new ImpellerRect { X = (float)src.X, Y = (float)src.Y, Width = (float)src.Width, Height = (float)src.Height };
        var dstRect = new ImpellerRect
        {
            X = options.DstBox.X,
            Y = options.DstBox.Y,
            Width = options.DstBox.Width,
            Height = options.DstBox.Height,
        };

        var paint = _renderer.TexturePaint;
        UnsafeNativeMethods.ImpellerPaintSetBlendModeRaw(paint, ImpellerBlendMode.kImpellerBlendModeSourceOver);
        var modulate = new ImpellerColor
        {
            Red = 1f,
            Green = 1f,
            Blue = 1f,
            Alpha = Math.Clamp(options.Alpha, 0f, 1f),
            Color_space = ImpellerColorSpace.kImpellerColorSpaceSRGB,
        };
        UnsafeNativeMethods.ImpellerPaintSetColorRaw(paint, &modulate);

        var transformed = !options.Transform.IsIdentity;
        var matrix = transformed ? ImpellerTransform.ToMatrix(options.Transform) : default;

        if (options.Clip is null)
        {
            if (transformed)
            {
                UnsafeNativeMethods.ImpellerDisplayListBuilderSaveRaw(_builder);
                ImpellerTransform.Apply(_builder, &matrix);
            }

            UnsafeNativeMethods.ImpellerDisplayListBuilderDrawTextureRectRaw(
                _builder, raw, &srcRect, &dstRect,
                ImpellerTextureSampling.kImpellerTextureSamplingLinear, paint);
            if (transformed)
            {
                UnsafeNativeMethods.ImpellerDisplayListBuilderRestoreRaw(_builder);
            }

            return;
        }

        foreach (var band in RegionRects.Of(options.Clip))
        {
            var clipRect = new ImpellerRect
            {
                X = band.X1,
                Y = band.Y1,
                Width = band.X2 - band.X1,
                Height = band.Y2 - band.Y1,
            };
            UnsafeNativeMethods.ImpellerDisplayListBuilderSaveRaw(_builder);
            UnsafeNativeMethods.ImpellerDisplayListBuilderClipRectRaw(
                _builder, &clipRect, ImpellerClipOperation.kImpellerClipOperationIntersect);
            if (transformed)
            {
                ImpellerTransform.Apply(_builder, &matrix);
            }

            UnsafeNativeMethods.ImpellerDisplayListBuilderDrawTextureRectRaw(
                _builder, raw, &srcRect, &dstRect,
                ImpellerTextureSampling.kImpellerTextureSamplingLinear, paint);
            UnsafeNativeMethods.ImpellerDisplayListBuilderRestoreRaw(_builder);
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

        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        for (var i = 0; i < vertices.Length; i++)
        {
            minX = Math.Min(minX, vertices[i].X);
            minY = Math.Min(minY, vertices[i].Y);
            maxX = Math.Max(maxX, vertices[i].X);
            maxY = Math.Max(maxY, vertices[i].Y);
        }

        if (maxX <= minX || maxY <= minY)
        {
            return;
        }

        var hull = new ImpellerRect { X = minX, Y = minY, Width = maxX - minX, Height = maxY - minY };
        var blend = options.Blend == RenderBlend.Additive
            ? ImpellerBlendMode.kImpellerBlendModePlus
            : ImpellerBlendMode.kImpellerBlendModeSourceOver;

        IntPtr raw = IntPtr.Zero;
        if (texture is not null && !((IImpellerGlTexture)texture).Acquire(out raw))
        {
            return;
        }

        if (texture is ImpellerGlDmabufTexture { SampledThisPass: false } dmabuf)
        {
            dmabuf.SampledThisPass = true;
            _sampled.Add(dmabuf);
        }

        IntPtr paint;
        ImpellerRect srcRect = default;
        if (texture is not null)
        {
            paint = _renderer.TexturePaint;
            UnsafeNativeMethods.ImpellerPaintSetBlendModeRaw(paint, blend);
            var opaque = new ImpellerColor
            {
                Red = 1f,
                Green = 1f,
                Blue = 1f,
                Alpha = 1f,
                Color_space = ImpellerColorSpace.kImpellerColorSpaceSRGB,
            };
            UnsafeNativeMethods.ImpellerPaintSetColorRaw(paint, &opaque);
            srcRect = new ImpellerRect { X = 0, Y = 0, Width = texture.Width, Height = texture.Height };
        }
        else
        {
            paint = _renderer.RectPaint;
            UnsafeNativeMethods.ImpellerPaintSetBlendModeRaw(paint, blend);
            SetPaintColor(paint, vertices[0].Color);
        }

        if (options.Clip is null)
        {
            DrawMeshHull(texture, raw, &srcRect, &hull, paint);
            return;
        }

        foreach (var band in RegionRects.Of(options.Clip))
        {
            var clipRect = new ImpellerRect
            {
                X = band.X1,
                Y = band.Y1,
                Width = band.X2 - band.X1,
                Height = band.Y2 - band.Y1,
            };
            UnsafeNativeMethods.ImpellerDisplayListBuilderSaveRaw(_builder);
            UnsafeNativeMethods.ImpellerDisplayListBuilderClipRectRaw(
                _builder, &clipRect, ImpellerClipOperation.kImpellerClipOperationIntersect);
            DrawMeshHull(texture, raw, &srcRect, &hull, paint);
            UnsafeNativeMethods.ImpellerDisplayListBuilderRestoreRaw(_builder);
        }
    }

    private void DrawMeshHull(ITexture? texture, IntPtr raw, ImpellerRect* srcRect, ImpellerRect* hull, IntPtr paint)
    {
        if (texture is not null)
        {
            UnsafeNativeMethods.ImpellerDisplayListBuilderDrawTextureRectRaw(
                _builder, raw, srcRect, hull,
                ImpellerTextureSampling.kImpellerTextureSamplingLinear, paint);
        }
        else
        {
            UnsafeNativeMethods.ImpellerDisplayListBuilderDrawRectRaw(_builder, hull, paint);
        }
    }

    private int _scopedSubmits;

    public bool Submit()
    {
        if (_scopedSubmits < 30)
        {
            _scopedSubmits++;
            return SubmitCore();
        }

        AllocationScope.Begin(region: "ImpellerSubmit", forgiving: true);
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
        var entry = _entry!;
        var builder = _builder;
        _target = null;
        _entry = null;
        _builder = IntPtr.Zero;

        var displayList = UnsafeNativeMethods.ImpellerDisplayListBuilderCreateDisplayListNewRaw(builder);
        UnsafeNativeMethods.ImpellerDisplayListBuilderRelease(builder);
        BasinCounters.Untrack();

        var gl = _renderer.Device.Gl;
        gl.BindFramebuffer(Silk.NET.OpenGLES.FramebufferTarget.ReadFramebuffer, entry.Native.Framebuffer);
        gl.BindTexture(Silk.NET.OpenGLES.TextureTarget.Texture2D, entry.SnapshotGlId);
        gl.CopyTexSubImage2D(
            Silk.NET.OpenGLES.TextureTarget.Texture2D, 0, 0, 0, 0, 0,
            (uint)target.Width, (uint)target.Height);

        var ok = false;
        if (displayList != IntPtr.Zero)
        {
            ok = UnsafeNativeMethods.ImpellerSurfaceDrawDisplayListRaw(entry.Surface, displayList) != 0;
            UnsafeNativeMethods.ImpellerDisplayListRelease(displayList);
        }

        if (entry.Native.IsCpuReadback)
        {
            ClearSampled();
            entry.Native.ReadInto(_renderer.Device, target);
        }
        else
        {
            PublishFence(entry, gl);
        }

        if (_signalFenceFd >= 0)
        {
            gl.Finish();
            RenderFences.SignalSyncobjFd(_renderer.Device.DrmFd, _signalFenceFd);
            _signalFenceFd = -1;
        }

        return ok;
    }

    private void PublishFence(ImpellerGlRenderer.TargetEntry entry, Silk.NET.OpenGLES.GL gl)
    {
        var fence = _renderer.Device.ExportFence();
        if (fence < 0)
        {
            gl.Flush();
            ClearSampled();
            return;
        }

        _renderer.ReplaceCompletionFence(RenderFences.DuplicateFence(fence));

        RenderFences.PublishFenceTo(entry.Native.Attributes, forWrite: true, fence);
        foreach (var texture in _sampled)
        {
            texture.SampledThisPass = false;
            RenderFences.PublishFenceTo(texture.Attributes, forWrite: false, fence);
        }

        _sampled.Clear();
        RenderFences.CloseFence(fence);
    }

    private void ClearSampled()
    {
        foreach (var texture in _sampled)
        {
            texture.SampledThisPass = false;
        }

        _sampled.Clear();
    }

    private static void SetPaintColor(IntPtr paint, in RenderColor color)
    {
        var a = Math.Clamp(color.A, 0f, 1f);
        var straight = a <= 0f
            ? new ImpellerColor { Color_space = ImpellerColorSpace.kImpellerColorSpaceSRGB }
            : new ImpellerColor
            {
                Red = Math.Clamp(color.R / a, 0f, 1f),
                Green = Math.Clamp(color.G / a, 0f, 1f),
                Blue = Math.Clamp(color.B / a, 0f, 1f),
                Alpha = a,
                Color_space = ImpellerColorSpace.kImpellerColorSpaceSRGB,
            };
        UnsafeNativeMethods.ImpellerPaintSetColorRaw(paint, &straight);
    }
}
