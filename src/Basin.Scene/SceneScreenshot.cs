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
