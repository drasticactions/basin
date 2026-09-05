using Pixman;
using SkiaSharp;

namespace Basin.Render.Skia;

internal static class SkiaDraw
{
    private static readonly SKSamplingOptions LinearSampling = new(SKFilterMode.Linear);

    public static void Rect(SKCanvas canvas, SKPaint paint, in RenderColor color, in Box box, PixmanRegion32? clip)
    {
        if (box.IsEmpty)
        {
            return;
        }

        paint.BlendMode = color.A >= 1f ? SKBlendMode.Src : SKBlendMode.SrcOver;
        SetPaintColor(paint, color);
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

    public static void Texture(
        SKCanvas canvas, SKPaint paint, ISkiaTexture texture, in TextureRenderOptions options,
        SkiaColorTransform? transform = null)
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

            if (options.Lut is SkiaColorLut lut)
            {
                paint.ColorFilter = lut.Filter;
            }
            else if (transform is not null)
            {
                paint.ColorFilter = transform.Filter;
            }

            var matrix = ToMatrix(options.Transform);
            if (options.Clip is null)
            {
                if (transformed)
                {
                    canvas.Save();
                    canvas.Concat(matrix);
                }

                canvas.DrawImage(image, srcRect, dstRect, LinearSampling, paint);
                if (transformed)
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

                canvas.DrawImage(image, srcRect, dstRect, LinearSampling, paint);
                canvas.Restore();
            }
        }
        finally
        {
            if (options.Lut is SkiaColorLut || transform is not null)
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
        paint.Shader = built;
        try
        {
            DrawShaderRect(canvas, paint, options.DstBox, options.Clip, transformed: false, default);
        }
        finally
        {
            paint.Shader = null;
        }
    }

    private static void DrawWithShader(SKCanvas canvas, SKPaint paint, SKImage image, in FBox src, in TextureRenderOptions options, SkiaColorTransform? transform)
    {
        if (options.Shader is not SkiaPixelShader skiaShader)
        {
            throw new ArgumentException("shader does not belong to this renderer");
        }

        if (!skiaShader.SamplesTexture)
        {
            throw new ArgumentException("shader does not sample a texture");
        }

        var child = SkiaCensus.Track(image.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, LinearSampling));
        if (options.Lut is SkiaColorLut lut)
        {
            var filtered = SkiaCensus.Track(child.WithColorFilter(lut.Filter));
            SkiaCensus.Release(child);
            child = filtered;
        }
        else if (transform is not null)
        {
            var filtered = SkiaCensus.Track(child.WithColorFilter(transform.Filter));
            SkiaCensus.Release(child);
            child = filtered;
        }

        SKShader? built = null;
        try
        {
            built = SkiaCensus.Track(skiaShader.RealizeWithChild(options.DstBox.Width, options.DstBox.Height, child, src));
            paint.BlendMode = SKBlendMode.SrcOver;
            paint.SetColor(new SKColorF(1f, 1f, 1f, Math.Clamp(options.Alpha, 0f, 1f)), null);
            paint.Shader = built;
            DrawShaderRect(canvas, paint, options.DstBox, options.Clip, !options.Transform.IsIdentity, ToMatrix(options.Transform));
        }
        finally
        {
            paint.Shader = null;
            SkiaCensus.Release(built);
            SkiaCensus.Release(child);
        }
    }

    private static void DrawShaderRect(SKCanvas canvas, SKPaint paint, in Box box, PixmanRegion32? clip, bool transformed, in SKMatrix matrix)
    {
        var rect = SKRect.Create(0f, 0f, box.Width, box.Height);
        if (clip is null)
        {
            canvas.Save();
            if (transformed)
            {
                canvas.Concat(matrix);
            }

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

            canvas.Translate(box.X, box.Y);
            canvas.DrawRect(rect, paint);
            canvas.Restore();
        }
    }

    private static SKPoint[] _positions = [];
    private static SKPoint[] _texCoords = [];
    private static SKColor[] _colors = [];

    public static void Mesh(
        SKCanvas canvas, SKPaint paint, ISkiaTexture? texture, ReadOnlySpan<MeshVertex> vertices, in MeshRenderOptions options)
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

        SKShader? shader = null;
        try
        {
            if (_positions.Length != vertices.Length)
            {
                _positions = new SKPoint[vertices.Length];
                _texCoords = new SKPoint[vertices.Length];
                _colors = new SKColor[vertices.Length];
            }

            for (var i = 0; i < vertices.Length; i++)
            {
                var vertex = vertices[i];
                _positions[i] = new SKPoint(vertex.X, vertex.Y);
                _texCoords[i] = new SKPoint(vertex.U, vertex.V);
                var a = Math.Clamp(vertex.Color.A, 0f, 1f);
                _colors[i] = a <= 0f
                    ? new SKColor(0, 0, 0, 0)
                    : new SKColor(
                        (byte)((Math.Clamp(vertex.Color.R / a, 0f, 1f) * 255f) + 0.5f),
                        (byte)((Math.Clamp(vertex.Color.G / a, 0f, 1f) * 255f) + 0.5f),
                        (byte)((Math.Clamp(vertex.Color.B / a, 0f, 1f) * 255f) + 0.5f),
                        (byte)((a * 255f) + 0.5f));
            }

            paint.BlendMode = options.Blend == RenderBlend.Additive ? SKBlendMode.Plus : SKBlendMode.SrcOver;
            paint.SetColor(new SKColorF(1f, 1f, 1f, 1f), null);
            if (image is not null)
            {
                shader = image.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, LinearSampling);
                paint.Shader = shader;
            }

            if (options.Clip is null)
            {
                canvas.DrawVertices(SKVertexMode.Triangles, _positions, _texCoords, _colors, SKBlendMode.Modulate, null!, paint);
                return;
            }

            foreach (var band in RegionRects.Of(options.Clip))
            {
                canvas.Save();
                canvas.ClipRect(new SKRect(band.X1, band.Y1, band.X2, band.Y2), SKClipOperation.Intersect, false);
                canvas.DrawVertices(SKVertexMode.Triangles, _positions, _texCoords, _colors, SKBlendMode.Modulate, null!, paint);
                canvas.Restore();
            }
        }
        finally
        {
            paint.Shader = null;
            shader?.Dispose();
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
