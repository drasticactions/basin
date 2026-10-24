using Basin.Diagnostics;

namespace Basin.Scene;

/// <summary>
/// Writes PNG files from the scene or from the buffers that an output presented.
/// </summary>
public static class SceneScreenshot
{
    /// <summary>
    /// Renders the scene as an output shows it, on a black background, and writes it as an 8-bit PNG file.
    /// </summary>
    /// <param name="scene">The scene to render.</param>
    /// <param name="renderer">The renderer that draws.</param>
    /// <param name="output">The output whose mode and projection to use.</param>
    /// <param name="path">The file to write.</param>
    /// <param name="cursor">The cursor to draw on top, if any.</param>
    /// <returns><see langword="false"/> when the render fails.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is <see langword="null"/>.</exception>
    public static bool Write(Scene scene, IRenderer renderer, IOutput output, string path, in CursorBlit cursor = default)
    {
        ArgumentNullException.ThrowIfNull(output);
        var mode = output.CurrentMode;
        return Write(scene, renderer, path, mode.Width, mode.Height, new SceneRenderOptions
        {
            Background = new RenderColor(0f, 0f, 0f, 1f),
            Projection = OutputProjection.For(output),
        }, cursor);
    }

    /// <summary>
    /// Renders the scene into an 8-bit buffer and writes it as a PNG file. An HDR color description in <paramref name="options"/> is replaced with the SDR default.
    /// </summary>
    /// <param name="scene">The scene to render.</param>
    /// <param name="renderer">The renderer that draws.</param>
    /// <param name="path">The file to write.</param>
    /// <param name="width">The image width, in physical pixels.</param>
    /// <param name="height">The image height, in physical pixels.</param>
    /// <param name="options">The background, projection and color settings.</param>
    /// <param name="cursor">The cursor to draw on top, if any.</param>
    /// <returns><see langword="false"/> when the render fails.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scene"/> or <paramref name="renderer"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is <see langword="null"/> or empty.</exception>
    public static bool Write(
        Scene scene,
        IRenderer renderer,
        string path,
        int width,
        int height,
        in SceneRenderOptions options,
        in CursorBlit cursor = default)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentException.ThrowIfNullOrEmpty(path);

        var sdr = options.ColorDescription?.TransferNamed is Capabilities.ColorTransferFunction.St2084Pq or Capabilities.ColorTransferFunction.Hlg
            ? options with { ColorDescription = Capabilities.ImageDescription.SdrDefault, Luts = null }
            : options;
        var target = new MemoryBuffer(width, height, DrmFormat.Xrgb8888);
        try
        {
            if (!scene.Render(renderer, target, sdr))
            {
                return false;
            }

            if (cursor.Buffer is { } sprite)
            {
                DrawCursor(renderer, target, sprite, cursor.Destination);
            }

            BufferCapture.WritePng(target, path);
            return true;
        }
        finally
        {
            target.Destroy();
        }
    }

    /// <summary>
    /// Renders the scene into a half-float buffer and writes it as a 16-bit PNG file with color and HDR metadata chunks.
    /// </summary>
    /// <param name="scene">The scene to render.</param>
    /// <param name="renderer">The renderer that draws.</param>
    /// <param name="path">The file to write.</param>
    /// <param name="width">The image width, in physical pixels.</param>
    /// <param name="height">The image height, in physical pixels.</param>
    /// <param name="options">The background, projection and color settings. The color description goes into the PNG.</param>
    /// <param name="hdr">The HDR static metadata to write, or <see langword="null"/>.</param>
    /// <param name="cursor">The cursor to draw on top, if any.</param>
    /// <returns><see langword="false"/> when the renderer cannot render half-float, the render fails, or the pixels cannot be read.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scene"/> or <paramref name="renderer"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is <see langword="null"/> or empty.</exception>
    public static bool Write16(
        Scene scene,
        IRenderer renderer,
        string path,
        int width,
        int height,
        in SceneRenderOptions options,
        HdrStaticMetadata? hdr = null,
        in CursorBlit cursor = default)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!renderer.RendersHalfFloat)
        {
            return false;
        }

        var target = new MemoryBuffer(width, height, DrmFormat.Abgr16161616f);
        try
        {
            if (!scene.Render(renderer, target, options))
            {
                return false;
            }

            if (cursor.Buffer is { } sprite)
            {
                DrawCursor(renderer, target, sprite, cursor.Destination);
            }

            if (!BufferCapture.TryReadRgb16(target, out var rgb))
            {
                return false;
            }

            File.WriteAllBytes(path, PngCodec.Encode16(rgb, width, height, PngColorChunks.From(options.ColorDescription, hdr)));
            return true;
        }
        finally
        {
            target.Destroy();
        }
    }

    /// <summary>
    /// Writes a presented buffer as a PNG file. Content on overlay planes is not included.
    /// </summary>
    /// <param name="presented">The buffer that the output last presented, or <see langword="null"/>.</param>
    /// <param name="renderer">The renderer that reads the buffer back.</param>
    /// <param name="path">The file to write.</param>
    /// <returns>The outcome. No file is written unless it is <see cref="ScreenshotOutcome.Written"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="renderer"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is <see langword="null"/> or empty.</exception>
    public static ScreenshotOutcome WritePresented(IBuffer? presented, IRenderer renderer, string path)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (presented is not { IsDestroyed: false })
        {
            return ScreenshotOutcome.NoFrame;
        }

        return BufferCapture.TryWritePng(presented, renderer, path)
            ? ScreenshotOutcome.Written
            : ScreenshotOutcome.Unreadable;
    }

    /// <summary>
    /// Writes a presented buffer as a PNG file, with the accepted overlay plane layers composited on top.
    /// </summary>
    /// <param name="presented">The buffer that the output last presented, or <see langword="null"/>.</param>
    /// <param name="layers">The overlay layers of the same commit, or <see langword="null"/>.</param>
    /// <param name="renderer">The renderer that reads and composites the buffers.</param>
    /// <param name="path">The file to write.</param>
    /// <returns>The outcome. No file is written unless it is <see cref="ScreenshotOutcome.Written"/>.</returns>
    public static ScreenshotOutcome WritePresented(
        IBuffer? presented, IReadOnlyList<OutputLayer>? layers, IRenderer renderer, string path) =>
        WritePresented(presented, layers, renderer, path, out _);

    /// <summary>
    /// Writes a presented buffer as a PNG file, with the accepted overlay plane layers composited on top, and counts those layers.
    /// </summary>
    /// <param name="presented">The buffer that the output last presented, or <see langword="null"/>.</param>
    /// <param name="layers">The overlay layers of the same commit, or <see langword="null"/>.</param>
    /// <param name="renderer">The renderer that reads and composites the buffers.</param>
    /// <param name="path">The file to write.</param>
    /// <param name="planes">The number of planes composited over the primary buffer.</param>
    /// <returns>The outcome. No file is written unless it is <see cref="ScreenshotOutcome.Written"/>.</returns>
    public static ScreenshotOutcome WritePresented(
        IBuffer? presented, IReadOnlyList<OutputLayer>? layers, IRenderer renderer, string path, out int planes) =>
        WritePresented(presented, layers, default, renderer, path, out planes);

    /// <summary>
    /// Writes a presented buffer as a PNG file, with the accepted overlay plane layers and the cursor plane composited on top.
    /// </summary>
    /// <param name="presented">The buffer that the output last presented, or <see langword="null"/>.</param>
    /// <param name="layers">The overlay layers of the same commit, or <see langword="null"/>.</param>
    /// <param name="cursor">The cursor plane image and position, if any.</param>
    /// <param name="renderer">The renderer that reads and composites the buffers.</param>
    /// <param name="path">The file to write.</param>
    /// <param name="planes">The number of planes composited over the primary buffer, the cursor included.</param>
    /// <returns>The outcome. No file is written unless it is <see cref="ScreenshotOutcome.Written"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="renderer"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is <see langword="null"/> or empty.</exception>
    public static ScreenshotOutcome WritePresented(
        IBuffer? presented,
        IReadOnlyList<OutputLayer>? layers,
        in CursorBlit cursor,
        IRenderer renderer,
        string path,
        out int planes)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentException.ThrowIfNullOrEmpty(path);

        planes = 0;
        if (presented is not { IsDestroyed: false })
        {
            return ScreenshotOutcome.NoFrame;
        }

        var onPlanes = 0;
        if (layers is not null)
        {
            for (var i = 0; i < layers.Count; i++)
            {
                if (layers[i] is { Accepted: true, Buffer: { IsDestroyed: false } })
                {
                    onPlanes++;
                }
            }
        }

        var hasCursor = cursor.Buffer is { IsDestroyed: false } && !cursor.Destination.IsEmpty;
        planes = onPlanes + (hasCursor ? 1 : 0);
        if (onPlanes == 0 && !hasCursor)
        {
            return BufferCapture.TryWritePng(presented, renderer, path)
                ? ScreenshotOutcome.Written
                : ScreenshotOutcome.Unreadable;
        }

        var target = new MemoryBuffer(presented.Width, presented.Height, DrmFormat.Xrgb8888);
        try
        {
            return Composite(presented, layers, cursor, renderer, target, path);
        }
        finally
        {
            target.Destroy();
        }
    }

    private static ScreenshotOutcome Composite(
        IBuffer presented,
        IReadOnlyList<OutputLayer>? layers,
        in CursorBlit cursor,
        IRenderer renderer,
        IBuffer target,
        string path)
    {
        if (renderer.ImportTexture(presented) is not { } primary)
        {
            return ScreenshotOutcome.Unreadable;
        }

        var pass = renderer.BeginBufferPass(target, new RenderPassOptions());
        var imported = new List<ITexture> { primary };
        try
        {
            pass.AddTexture(primary, new TextureRenderOptions
            {
                DstBox = new Box(0, 0, target.Width, target.Height),
                Opaque = true,
            });

            for (var i = 0; layers is not null && i < layers.Count; i++)
            {
                var layer = layers[i];
                if (layer is not { Accepted: true, Buffer: { IsDestroyed: false } buffer } ||
                    layer.DstBox.IsEmpty ||
                    renderer.ImportTexture(buffer) is not { } texture)
                {
                    continue;
                }

                imported.Add(texture);
                pass.AddTexture(texture, new TextureRenderOptions
                {
                    SrcBox = layer.SrcBox,
                    DstBox = layer.DstBox,
                    Alpha = layer.Alpha,
                    Opaque = layer.Opaque,
                });
            }

            if (cursor.Buffer is { IsDestroyed: false } sprite && !cursor.Destination.IsEmpty &&
                renderer.ImportTexture(sprite) is { } cursorTexture)
            {
                imported.Add(cursorTexture);
                pass.AddTexture(cursorTexture, new TextureRenderOptions { DstBox = cursor.Destination });
            }

            if (!pass.Submit())
            {
                return ScreenshotOutcome.Unreadable;
            }
        }
        finally
        {
            foreach (var texture in imported)
            {
                texture.Dispose();
            }
        }

        BufferCapture.WritePng(target, path);
        return ScreenshotOutcome.Written;
    }

    private static void DrawCursor(IRenderer renderer, IBuffer target, IBuffer sprite, in Box destination)
    {
        if (renderer.ImportTexture(sprite) is not { } texture)
        {
            return;
        }

        try
        {
            var pass = renderer.BeginBufferPass(target, new RenderPassOptions());
            pass.AddTexture(texture, new TextureRenderOptions { DstBox = destination });
            pass.Submit();
        }
        finally
        {
            texture.Dispose();
        }
    }
}
