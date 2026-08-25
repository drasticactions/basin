using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Basin;
using Basin.Cli;

namespace PlasmaHost;

internal sealed class BreezeIcons : IDisposable
{
    private static readonly IconSearch IconPngs = new()
    {
        Extensions = [".png"],
        Sizes = [48, 32],
    };

    private readonly Dictionary<string, Bitmap?> _named = [];
    private readonly Dictionary<IBuffer, Bitmap?> _pixels = [];
    private bool _disposed;

    public Bitmap? For(string name)
    {
        if (_named.TryGetValue(name, out var cached))
        {
            return cached;
        }

        Bitmap? bitmap = null;
        if (IconPngs.Find(name) is { } path)
        {
            try
            {
                bitmap = new Bitmap(path);
            }
            catch (Exception)
            {
                bitmap = null;
            }
        }

        _named[name] = bitmap;
        return bitmap;
    }

    public Bitmap? For(IBuffer buffer)
    {
        if (_pixels.TryGetValue(buffer, out var cached))
        {
            return cached;
        }

        Bitmap? bitmap = null;
        if (buffer.BeginDataAccess(BufferDataAccess.Read, out var view))
        {
            try
            {
                bitmap = new Bitmap(
                    PixelFormats.Bgra8888,
                    AlphaFormat.Premul,
                    view.Data,
                    new PixelSize(buffer.Width, buffer.Height),
                    new Vector(96, 96),
                    view.Stride);
            }
            catch (Exception)
            {
                bitmap = null;
            }
            finally
            {
                buffer.EndDataAccess();
            }
        }

        _pixels[buffer] = bitmap;
        buffer.Destroyed += () =>
        {
            if (_pixels.Remove(buffer, out var dead))
            {
                dead?.Dispose();
            }
        };
        return bitmap;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var bitmap in _named.Values)
        {
            bitmap?.Dispose();
        }

        _named.Clear();
        foreach (var bitmap in _pixels.Values)
        {
            bitmap?.Dispose();
        }

        _pixels.Clear();
    }
}
