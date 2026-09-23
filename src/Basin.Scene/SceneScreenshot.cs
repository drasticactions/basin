using Basin.Diagnostics;

namespace Basin.Scene;

public static class SceneScreenshot
{
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

        var target = new MemoryBuffer(width, height, DrmFormat.Xrgb8888);
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

            BufferCapture.WritePng(target, path);
            return true;
        }
        finally
        {
            target.Destroy();
        }
    }

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

    public static ScreenshotOutcome WritePresented(
        IBuffer? presented, IReadOnlyList<OutputLayer>? layers, IRenderer renderer, string path) =>
        WritePresented(presented, layers, renderer, path, out _);

    public static ScreenshotOutcome WritePresented(
        IBuffer? presented, IReadOnlyList<OutputLayer>? layers, IRenderer renderer, string path, out int planes) =>
        WritePresented(presented, layers, default, renderer, path, out planes);

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
