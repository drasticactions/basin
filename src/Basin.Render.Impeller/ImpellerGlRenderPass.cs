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
    private ImpellerBlendMode _meshBlend;

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

        var blend = options.Blend == RenderBlend.Additive
            ? ImpellerBlendMode.kImpellerBlendModePlus
            : ImpellerBlendMode.kImpellerBlendModeSourceOver;
        _meshBlend = blend;

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
            DrawTriangles(vertices, raw, &srcRect, paint);
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
            DrawTriangles(vertices, raw, &srcRect, paint);
            UnsafeNativeMethods.ImpellerDisplayListBuilderRestoreRaw(_builder);
        }
    }

    private void DrawTriangles(ReadOnlySpan<MeshVertex> vertices, IntPtr texture, ImpellerRect* srcRect, IntPtr paint)
    {
        var path = _renderer.PathBuilder;
        if (texture == IntPtr.Zero)
        {
            var hasColor = false;
            RenderColor last = default;
            for (var i = 0; i < vertices.Length; i += 3)
            {
                if (!AddTriangle(path, vertices[i], vertices[i + 1], vertices[i + 2]))
                {
                    continue;
                }

                var color = Mean(vertices[i].Color, vertices[i + 1].Color, vertices[i + 2].Color);
                if (!hasColor || color != last)
                {
                    SetPaintColor(paint, color);
                    last = color;
                    hasColor = true;
                }

                var fill = UnsafeNativeMethods.ImpellerPathBuilderTakePathNewRaw(path, ImpellerFillType.kImpellerFillTypeNonZero);
                UnsafeNativeMethods.ImpellerDisplayListBuilderDrawPathRaw(_builder, fill, paint);
                UnsafeNativeMethods.ImpellerPathRelease(fill);
            }

            return;
        }

        for (var i = 0; i < vertices.Length; i += 3)
        {
            var a = vertices[i];
            var b = vertices[i + 1];
            var c = vertices[i + 2];
            if (!SourceToTarget(a, b, c, out var matrix) || !AddTriangle(path, a, b, c))
            {
                continue;
            }

            var clip = UnsafeNativeMethods.ImpellerPathBuilderTakePathNewRaw(path, ImpellerFillType.kImpellerFillTypeNonZero);
            UnsafeNativeMethods.ImpellerDisplayListBuilderSaveRaw(_builder);
            UnsafeNativeMethods.ImpellerDisplayListBuilderClipPathRaw(_builder, clip, ImpellerClipOperation.kImpellerClipOperationIntersect);
            ImpellerTransform.Apply(_builder, &matrix);
            var color = Mean(a.Color, b.Color, c.Color);
            var drawPaint = paint;
            var filter = IntPtr.Zero;
            if (color.R < 1f || color.G < 1f || color.B < 1f || color.A < 1f)
            {
                var alpha = Math.Clamp(color.A, 0f, 1f);
                var straight = new ImpellerColor
                {
                    Red = alpha <= 0f ? 0f : Math.Clamp(color.R / alpha, 0f, 1f),
                    Green = alpha <= 0f ? 0f : Math.Clamp(color.G / alpha, 0f, 1f),
                    Blue = alpha <= 0f ? 0f : Math.Clamp(color.B / alpha, 0f, 1f),
                    Alpha = alpha,
                    Color_space = ImpellerColorSpace.kImpellerColorSpaceSRGB,
                };
                filter = ImpellerColorFilters.CreateBlend(&straight, ImpellerBlendMode.kImpellerBlendModeModulate);
                if (filter != IntPtr.Zero)
                {
                    drawPaint = _renderer.ModulatePaint;
                    UnsafeNativeMethods.ImpellerPaintSetBlendModeRaw(drawPaint, _meshBlend);
                    ImpellerColorFilters.SetOnPaint(drawPaint, filter);
                }
            }

            UnsafeNativeMethods.ImpellerDisplayListBuilderDrawTextureRectRaw(
                _builder, texture, srcRect, srcRect,
                ImpellerTextureSampling.kImpellerTextureSamplingLinear, drawPaint);
            if (filter != IntPtr.Zero)
            {
                ImpellerColorFilters.Release(filter);
            }

            UnsafeNativeMethods.ImpellerDisplayListBuilderRestoreRaw(_builder);
            UnsafeNativeMethods.ImpellerPathRelease(clip);
        }
    }

    private static RenderColor Mean(in RenderColor a, in RenderColor b, in RenderColor c) =>
        a == b && b == c
            ? a
            : new RenderColor((a.R + b.R + c.R) / 3f, (a.G + b.G + c.G) / 3f, (a.B + b.B + c.B) / 3f, (a.A + b.A + c.A) / 3f);

    private static bool AddTriangle(IntPtr path, in MeshVertex a, in MeshVertex b, in MeshVertex c)
    {
        var area = ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));
        if (area == 0 || !float.IsFinite(area))
        {
            return false;
        }

        var first = new ImpellerPoint { X = a.X, Y = a.Y };
        var second = area > 0 ? new ImpellerPoint { X = b.X, Y = b.Y } : new ImpellerPoint { X = c.X, Y = c.Y };
        var third = area > 0 ? new ImpellerPoint { X = c.X, Y = c.Y } : new ImpellerPoint { X = b.X, Y = b.Y };
        UnsafeNativeMethods.ImpellerPathBuilderMoveToRaw(path, &first);
        UnsafeNativeMethods.ImpellerPathBuilderLineToRaw(path, &second);
        UnsafeNativeMethods.ImpellerPathBuilderLineToRaw(path, &third);
        UnsafeNativeMethods.ImpellerPathBuilderCloseRaw(path);
        return true;
    }

    private static bool SourceToTarget(in MeshVertex a, in MeshVertex b, in MeshVertex c, out ImpellerMatrix matrix)
    {
        double du1 = b.U - a.U, dv1 = b.V - a.V, du2 = c.U - a.U, dv2 = c.V - a.V;
        var det = (du1 * dv2) - (du2 * dv1);
        if (det == 0 || !double.IsFinite(det))
        {
            matrix = default;
            return false;
        }

        double dx1 = b.X - a.X, dy1 = b.Y - a.Y, dx2 = c.X - a.X, dy2 = c.Y - a.Y;
        var m11 = ((dx1 * dv2) - (dx2 * dv1)) / det;
        var m12 = ((dx2 * du1) - (dx1 * du2)) / det;
        var m21 = ((dy1 * dv2) - (dy2 * dv1)) / det;
        var m22 = ((dy2 * du1) - (dy1 * du2)) / det;
        var m13 = a.X - (m11 * a.U) - (m12 * a.V);
        var m23 = a.Y - (m21 * a.U) - (m22 * a.V);
        matrix = new ImpellerMatrix
        {
            Matrix = new System.Numerics.Matrix4x4(
                (float)m11, (float)m21, 0f, 0f,
                (float)m12, (float)m22, 0f, 0f,
                0f, 0f, 1f, 0f,
                (float)m13, (float)m23, 0f, 1f),
        };
        return true;
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
