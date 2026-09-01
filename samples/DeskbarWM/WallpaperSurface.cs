using Basin.WindowManager;
using Basin.WindowManager.Skia;
using Basin.WindowManager.Skia.Protocol;
using SkiaSharp;
using Wayland;

namespace DeskbarWm;

internal sealed class WallpaperSurface : IDisposable
{
    private readonly ManagerSurface _surface;
    private SKImage? _image;
    private string? _imagePath;
    private string? _lastKey;

    internal WallpaperSurface(
        WlCompositor compositor,
        WlShm shm,
        ZwlrLayerShellV1 layerShell,
        WlOutput wlOutput,
        WmOutput output,
        RiverWindowManager wm)
    {
        Output = output;
        _surface = new ManagerSurface(
            compositor, shm, layerShell, wlOutput, ZwlrLayerShellV1.Layer.Background, "deskbar-wallpaper");
        _surface.SetExclusiveZone(-1);
        _surface.SetAnchor(
            ZwlrLayerSurfaceV1.Anchor.Top | ZwlrLayerSurfaceV1.Anchor.Bottom
            | ZwlrLayerSurfaceV1.Anchor.Left | ZwlrLayerSurfaceV1.Anchor.Right);
        _surface.Configured += wm.RequestManage;
        _surface.CommitInitial();
    }

    public WmOutput Output { get; }

    public uint SurfaceId => _surface.SurfaceId;

    public void Invalidate() => _lastKey = null;

    public bool Render(Config config, int scale)
    {
        if (!_surface.IsConfigured || _surface.ConfiguredSize.IsEmpty)
        {
            return false;
        }

        var size = _surface.ConfiguredSize;
        var key = $"{size.Width}x{size.Height}|{scale}|{config.DesktopWallpaper}|{config.DesktopColor}|{config.DesktopScaleMode}";
        if (key == _lastKey)
        {
            return false;
        }

        EnsureImage(config.DesktopWallpaper);

        var pixels = _surface.Prepare(size.Width, size.Height, scale);
        if (pixels == 0)
        {
            return false;
        }

        using var canvas = _surface.CreateCanvas(pixels);
        if (canvas is null)
        {
            return false;
        }

        canvas.Canvas.Scale(scale);
        Draw(canvas.Canvas, size, config);
        canvas.Canvas.Flush();
        _surface.SetInputRegion(new Rect(0, 0, size.Width, size.Height));
        _lastKey = key;
        return true;
    }

    public void Commit() => _surface.Commit();

    public void Dispose()
    {
        _image?.Dispose();
        _surface.Dispose();
    }

    private void EnsureImage(string path)
    {
        if (path == _imagePath)
        {
            return;
        }

        _imagePath = path;
        _image?.Dispose();
        _image = null;
        if (path.Length == 0 || !File.Exists(path))
        {
            return;
        }

        try
        {
            using var bitmap = SKBitmap.Decode(path);
            if (bitmap is not null && bitmap.Width > 0 && bitmap.Height > 0)
            {
                _image = SKImage.FromBitmap(bitmap);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void Draw(SKCanvas canvas, Size size, Config config)
    {
        var color = config.DesktopColor;
        canvas.Clear(new SKColor(
            (byte)(color >> 24), (byte)(color >> 16), (byte)(color >> 8), (byte)color));

        if (_image is not { } image)
        {
            return;
        }

        var sampling = new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear);
        switch (config.DesktopScaleMode)
        {
            case "center":
                canvas.DrawImage(
                    image,
                    (size.Width - image.Width) / 2f,
                    (size.Height - image.Height) / 2f,
                    sampling);
                break;
            case "tile":
                for (var y = 0; y < size.Height; y += image.Height)
                {
                    for (var x = 0; x < size.Width; x += image.Width)
                    {
                        canvas.DrawImage(image, x, y, sampling);
                    }
                }

                break;
            case "fit":
            {
                var fit = MathF.Min(size.Width / (float)image.Width, size.Height / (float)image.Height);
                var width = image.Width * fit;
                var height = image.Height * fit;
                canvas.DrawImage(
                    image,
                    new SKRect(
                        (size.Width - width) / 2f,
                        (size.Height - height) / 2f,
                        (size.Width + width) / 2f,
                        (size.Height + height) / 2f),
                    sampling);
                break;
            }

            default:
            {
                var fill = MathF.Max(size.Width / (float)image.Width, size.Height / (float)image.Height);
                var width = image.Width * fill;
                var height = image.Height * fill;
                canvas.DrawImage(
                    image,
                    new SKRect(
                        (size.Width - width) / 2f,
                        (size.Height - height) / 2f,
                        (size.Width + width) / 2f,
                        (size.Height + height) / 2f),
                    sampling);
                break;
            }
        }
    }
}
