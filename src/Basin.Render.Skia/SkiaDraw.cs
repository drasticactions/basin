using Pixman;
using SkiaSharp;

namespace Basin.Render.Skia;

internal static class SkiaDraw
{
    private static readonly SKSamplingOptions LinearSampling = new(SKFilterMode.Linear);

    private static readonly SKSamplingOptions NearestSampling = new(SKFilterMode.Nearest);

    public static void Rect(SKCanvas canvas, SKPaint paint, in RenderColor color, in Box box, PixmanRegion32? clip, SKColorFilter? solid = null)
    {
        if (box.IsEmpty)
        {
            return;
        }

        paint.BlendMode = color.A >= 1f ? SKBlendMode.Src : SKBlendMode.SrcOver;
        SetPaintColor(paint, color);
        paint.ColorFilter = solid;
        try
        {
            var rect = SKRect.Create(box.X, box.Y, box.Width, box.Height);
            if (clip is null)
            {
                canvas.DrawRect(rect, paint);
                return;
            }

            foreach (var band in RegionRects.Of(clip))
            {
                canvas.Save();
                canvas.ClipRect(new SKRect(band.X1, band.Y1, band.X2, band.Y2), SKClipOperation.Intersect, false);
                canvas.DrawRect(rect, paint);
                canvas.Restore();
            }
        }
        finally
        {
            paint.ColorFilter = null;
        }
    }

    public static void Clear(SKCanvas canvas, in RenderColor color) => Clear(canvas, null, color, null);

    public static void Clear(SKCanvas canvas, SKPaint? paint, in RenderColor color, SKColorFilter? solid)
    {
        canvas.Save();
        canvas.ResetMatrix();
        if (solid is null || paint is null)
        {
            canvas.Clear(new SKColorF(color.R, color.G, color.B, color.A));
        }
        else
        {
            paint.BlendMode = SKBlendMode.Src;
            SetPaintColor(paint, color);
            paint.ColorFilter = solid;
            canvas.DrawPaint(paint);
            paint.ColorFilter = null;
            paint.BlendMode = SKBlendMode.SrcOver;
        }

        canvas.Restore();
    }

    public static SKColorFilter CreateOpaqueAlpha() => SKColorFilter.CreateColorMatrix(
    [
        1f, 0f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f, 0f,
        0f, 0f, 1f, 0f, 0f,
        0f, 0f, 0f, 0f, 1f,
    ]);

    public static void Texture(
        SKCanvas canvas, SKPaint paint, ISkiaTexture texture, in TextureRenderOptions options,
        SkiaColorTransform? transform = null, SKColorFilter? opaqueAlpha = null)
    {
        if (options.DstBox.IsEmpty)
        {
            return;
        }

        var transformed = !options.Transform.IsIdentity;
        if (transformed && !options.Transform.TryInvert(out _))
        {
            return;
        }

        if (!texture.Acquire(out var image))
        {
            return;
        }

        nint modulation = 0;
        try
        {
            var src = options.SrcBox.IsEmpty
                ? new FBox(0, 0, texture.Width, texture.Height)
                : options.SrcBox;

            if (options.Shader is not null)
            {
                DrawWithShader(canvas, paint, image, src, options, transform);
                return;
            }

            var srcRect = SKRect.Create((float)src.X, (float)src.Y, (float)src.Width, (float)src.Height);
            var dstRect = SKRect.Create(options.DstBox.X, options.DstBox.Y, options.DstBox.Width, options.DstBox.Height);

            paint.BlendMode = SKBlendMode.SrcOver;
            paint.SetColor(new SKColorF(1f, 1f, 1f, Math.Clamp(options.Alpha, 0f, 1f)), null);

            SKColorFilter? chosen = null;
            if (options.Lut is SkiaColorLut lut)
            {
                chosen = lut.Filter;
            }
            else if (transform is not null)
            {
                chosen = transform.Filter;
            }
            else if (opaqueAlpha is not null && options.Alpha >= 1f && image.AlphaType == SKAlphaType.Opaque)
            {
                chosen = opaqueAlpha;
            }

            modulation = Modulation(options, chosen);
            if (modulation != 0)
            {
                SkiaShaderNative.SetPaintColorFilter(paint.Handle, modulation);
            }
            else if (chosen is not null)
            {
                paint.ColorFilter = chosen;
            }

            var matrix = ToMatrix(options.Transform);
            var sampling = options.Filter != TextureFilter.Linear ? NearestSampling : LinearSampling;
            if (options.Clip is null)
            {
                var saved = transformed || !options.Corners.IsEmpty;
                if (saved)
                {
                    canvas.Save();
                }

                if (transformed)
                {
                    canvas.Concat(matrix);
                }

                ClipCorners(canvas, options.DstBox, options.Corners);
                canvas.DrawImage(image, srcRect, dstRect, sampling, paint);
                if (saved)
                {
                    canvas.Restore();
                }

                return;
            }

            foreach (var band in RegionRects.Of(options.Clip))
            {
                canvas.Save();
                canvas.ClipRect(new SKRect(band.X1, band.Y1, band.X2, band.Y2), SKClipOperation.Intersect, false);
                if (transformed)
                {
                    canvas.Concat(matrix);
                }

                ClipCorners(canvas, options.DstBox, options.Corners);
                canvas.DrawImage(image, srcRect, dstRect, sampling, paint);
                canvas.Restore();
            }
        }
        finally
        {
            if (modulation != 0)
            {
                SkiaShaderNative.SetPaintColorFilter(paint.Handle, 0);
                SkiaShaderNative.UnrefColorFilter(modulation);
            }
            else if (paint.ColorFilter is not null)
            {
                paint.ColorFilter = null;
            }

            texture.Release();
        }
    }

    public static void Shader(SKCanvas canvas, SKPaint paint, IPixelShader shader, in ShaderRenderOptions options)
    {
        if (shader is not SkiaPixelShader skiaShader)
        {
            throw new ArgumentException("shader does not belong to this renderer");
        }

        if (skiaShader.SamplesTexture)
        {
            throw new ArgumentException("shader samples a texture and must draw through AddTexture");
        }

        if (options.DstBox.IsEmpty)
        {
            return;
        }

        var built = skiaShader.Realize(options.DstBox.Width, options.DstBox.Height);
        paint.BlendMode = SKBlendMode.SrcOver;
        paint.SetColor(new SKColorF(1f, 1f, 1f, Math.Clamp(options.Alpha, 0f, 1f)), null);
        SkiaShaderNative.SetPaintShader(paint.Handle, built);
        try
        {
            DrawShaderRect(canvas, paint, options.DstBox, options.Clip, transformed: false, default);
        }
        finally
        {
            SkiaShaderNative.SetPaintShader(paint.Handle, 0);
        }
    }

    private static unsafe void DrawWithShader(SKCanvas canvas, SKPaint paint, SKImage image, in FBox src, in TextureRenderOptions options, SkiaColorTransform? transform)
    {
        if (options.Shader is not SkiaPixelShader skiaShader)
        {
            throw new ArgumentException("shader does not belong to this renderer");
        }

        if (!skiaShader.SamplesTexture)
        {
            throw new ArgumentException("shader does not sample a texture");
        }

        var sampling = options.Filter != TextureFilter.Linear ? NearestSampling : LinearSampling;
        var child = SkiaShaderNative.ImageShader(image.Handle, SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, &sampling, null);
        if (child == 0)
        {
            throw new InvalidOperationException("image refused to make a shader");
        }

        var filter = options.Lut is SkiaColorLut lut ? lut.Filter : transform?.Filter;
        if (filter is not null)
        {
            var filtered = SkiaShaderNative.WithColorFilter(child, filter.Handle);
            SkiaShaderNative.UnrefShader(child);
            child = filtered;
        }

        nint built = 0;
        nint modulation = 0;
        try
        {
            built = skiaShader.RealizeWithChild(options.DstBox.Width, options.DstBox.Height, child, src);
            paint.BlendMode = SKBlendMode.SrcOver;
            paint.SetColor(new SKColorF(1f, 1f, 1f, Math.Clamp(options.Alpha, 0f, 1f)), null);
            SkiaShaderNative.SetPaintShader(paint.Handle, built);
            modulation = Modulation(options, null);
            if (modulation != 0)
            {
                SkiaShaderNative.SetPaintColorFilter(paint.Handle, modulation);
            }

            DrawShaderRect(canvas, paint, options.DstBox, options.Clip, !options.Transform.IsIdentity, ToMatrix(options.Transform), options.Corners);
        }
        finally
        {
            SkiaShaderNative.SetPaintShader(paint.Handle, 0);
            if (modulation != 0)
            {
                SkiaShaderNative.SetPaintColorFilter(paint.Handle, 0);
                SkiaShaderNative.UnrefColorFilter(modulation);
            }

            if (built != 0)
            {
                SkiaShaderNative.UnrefShader(built);
            }

            SkiaShaderNative.UnrefShader(child);
            GC.KeepAlive(image);
        }
    }

    private static void DrawShaderRect(SKCanvas canvas, SKPaint paint, in Box box, PixmanRegion32? clip, bool transformed, in SKMatrix matrix, CornerClip corners = default)
    {
        var rect = SKRect.Create(0f, 0f, box.Width, box.Height);
        if (clip is null)
        {
            canvas.Save();
            if (transformed)
            {
                canvas.Concat(matrix);
            }

            ClipCorners(canvas, box, corners);
            canvas.Translate(box.X, box.Y);
            canvas.DrawRect(rect, paint);
            canvas.Restore();
            return;
        }

        foreach (var band in RegionRects.Of(clip))
        {
            canvas.Save();
            canvas.ClipRect(new SKRect(band.X1, band.Y1, band.X2, band.Y2), SKClipOperation.Intersect, false);
            if (transformed)
            {
                canvas.Concat(matrix);
            }

            ClipCorners(canvas, box, corners);
            canvas.Translate(box.X, box.Y);
            canvas.DrawRect(rect, paint);
            canvas.Restore();
        }
    }

    private static unsafe void ClipCorners(SKCanvas canvas, in Box box, in CornerClip corners)
    {
        if (corners.IsEmpty)
        {
            return;
        }

        var outer = corners.OuterWidth > 0 && corners.OuterHeight > 0;
        var x = box.X - (outer ? corners.OffsetX : 0);
        var y = box.Y - (outer ? corners.OffsetY : 0);
        var rect = SKRect.Create((float)x, (float)y, (float)(outer ? corners.OuterWidth : box.Width), (float)(outer ? corners.OuterHeight : box.Height));
        var shape = SkiaShaderNative.NewRoundRect();
        try
        {
            SkiaShaderNative.SetRoundRect(shape, &rect, (float)corners.Radius, (float)corners.Radius);
            SkiaShaderNative.ClipRoundRect(canvas.Handle, shape, SKClipOperation.Intersect, true);
        }
        finally
        {
            SkiaShaderNative.DeleteRoundRect(shape);
        }
    }

    private static unsafe nint Modulation(in TextureRenderOptions options, SKColorFilter? inner)
    {
        var brightness = options.Brightness;
        var saturation = options.Saturation;
        if (brightness == 1f && saturation == 1f)
        {
            return 0;
        }

        const float LumaR = 0.2126f;
        const float LumaG = 0.7152f;
        const float LumaB = 0.0722f;
        var gray = 1f - saturation;
        var matrix = stackalloc float[20];
        matrix[0] = brightness * ((LumaR * gray) + saturation);
        matrix[1] = brightness * LumaG * gray;
        matrix[2] = brightness * LumaB * gray;
        matrix[3] = 0f;
        matrix[4] = 0f;
        matrix[5] = brightness * LumaR * gray;
        matrix[6] = brightness * ((LumaG * gray) + saturation);
        matrix[7] = brightness * LumaB * gray;
        matrix[8] = 0f;
        matrix[9] = 0f;
        matrix[10] = brightness * LumaR * gray;
        matrix[11] = brightness * LumaG * gray;
        matrix[12] = brightness * ((LumaB * gray) + saturation);
        matrix[13] = 0f;
        matrix[14] = 0f;
        matrix[15] = 0f;
        matrix[16] = 0f;
        matrix[17] = 0f;
        matrix[18] = 1f;
        matrix[19] = 0f;
        var filter = SkiaShaderNative.ColorMatrix(matrix);
        if (inner is null || filter == 0)
        {
            return filter;
        }

        var composed = SkiaShaderNative.ComposeColorFilters(filter, inner.Handle);
        SkiaShaderNative.UnrefColorFilter(filter);
        return composed;
    }

    private static SKPoint[] _positions = [];
    private static SKPoint[] _texCoords = [];
    private static uint[] _colors = [];

    public static void Mesh(
        SKCanvas canvas, SKPaint paint, ISkiaTexture? texture, ReadOnlySpan<MeshVertex> vertices, in MeshRenderOptions options,
        SKColorFilter? additive = null, SKColorFilter? solid = null)
    {
        if (vertices.Length == 0)
        {
            return;
        }

        if (vertices.Length % 3 != 0)
        {
            throw new ArgumentException("vertices must be a whole number of triangles", nameof(vertices));
        }

        SKImage? image = null;
        if (texture is not null && !texture.Acquire(out image))
        {
            return;
        }

        try
        {
            if (_positions.Length < vertices.Length)
            {
                _positions = new SKPoint[vertices.Length];
                _texCoords = new SKPoint[vertices.Length];
                _colors = new uint[vertices.Length];
            }

            var adds = options.Blend == RenderBlend.Additive;
            for (var i = 0; i < vertices.Length; i++)
            {
                var vertex = vertices[i];
                _positions[i] = new SKPoint(vertex.X, vertex.Y);
                _texCoords[i] = new SKPoint(vertex.U, vertex.V);
                if (adds)
                {
                    _colors[i] = 0xFF000000u
                        | (uint)(byte)((Math.Clamp(vertex.Color.R, 0f, 1f) * 255f) + 0.5f) << 16
                        | (uint)(byte)((Math.Clamp(vertex.Color.G, 0f, 1f) * 255f) + 0.5f) << 8
                        | (byte)((Math.Clamp(vertex.Color.B, 0f, 1f) * 255f) + 0.5f);
                    continue;
                }

                var a = Math.Clamp(vertex.Color.A, 0f, 1f);
                _colors[i] = a <= 0f
                    ? 0u
                    : (uint)(byte)((a * 255f) + 0.5f) << 24
                        | (uint)(byte)((Math.Clamp(vertex.Color.R / a, 0f, 1f) * 255f) + 0.5f) << 16
                        | (uint)(byte)((Math.Clamp(vertex.Color.G / a, 0f, 1f) * 255f) + 0.5f) << 8
                        | (byte)((Math.Clamp(vertex.Color.B / a, 0f, 1f) * 255f) + 0.5f);
            }

            paint.BlendMode = adds && additive is null ? SKBlendMode.Plus : SKBlendMode.SrcOver;
            if (adds && additive is not null)
            {
                paint.ColorFilter = additive;
            }
            else if (!adds && texture is null && solid is not null)
            {
                paint.ColorFilter = solid;
            }

            paint.SetColor(new SKColorF(1f, 1f, 1f, 1f), null);
            if (image is not null)
            {
                paint.Shader = texture!.MeshShader(image);
            }

            nint native;
            unsafe
            {
                fixed (SKPoint* positions = _positions)
                fixed (SKPoint* texCoords = _texCoords)
                fixed (uint* colors = _colors)
                {
                    native = SkiaVerticesNative.MakeCopy(
                        (int)SKVertexMode.Triangles, vertices.Length, positions, texCoords, colors, 0, null);
                }
            }

            if (native == 0)
            {
                return;
            }

            try
            {
                if (options.Clip is null)
                {
                    SkiaVerticesNative.Draw(canvas.Handle, native, (int)SKBlendMode.Modulate, paint.Handle);
                    return;
                }

                foreach (var band in RegionRects.Of(options.Clip))
                {
                    canvas.Save();
                    canvas.ClipRect(new SKRect(band.X1, band.Y1, band.X2, band.Y2), SKClipOperation.Intersect, false);
                    SkiaVerticesNative.Draw(canvas.Handle, native, (int)SKBlendMode.Modulate, paint.Handle);
                    canvas.Restore();
                }
            }
            finally
            {
                SkiaVerticesNative.Unref(native);
            }
        }
        finally
        {
            paint.Shader = null;
            paint.ColorFilter = null;
            paint.BlendMode = SKBlendMode.SrcOver;
            texture?.Release();
        }
    }

    private static SKMatrix ToMatrix(in RenderTransform transform) => new(
        (float)transform.M11, (float)transform.M12, (float)transform.M13,
        (float)transform.M21, (float)transform.M22, (float)transform.M23,
        (float)transform.M31, (float)transform.M32, (float)transform.M33);

    private static void SetPaintColor(SKPaint paint, in RenderColor color)
    {
        var a = Math.Clamp(color.A, 0f, 1f);
        if (a <= 0f)
        {
            paint.SetColor(new SKColorF(0f, 0f, 0f, 0f), null);
            return;
        }

        paint.SetColor(
            new SKColorF(
                Math.Clamp(color.R / a, 0f, 1f),
                Math.Clamp(color.G / a, 0f, 1f),
                Math.Clamp(color.B / a, 0f, 1f),
                a),
            null);
    }
}
