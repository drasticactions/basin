using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Basin.UI.Avalonia;

internal sealed class BasinIconLoader : IPlatformIconLoader
{
    public IWindowIconImpl LoadIcon(string fileName)
    {
        using var stream = File.OpenRead(fileName);
        return LoadIcon(stream);
    }

    public IWindowIconImpl LoadIcon(Stream stream)
    {
        var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return new BasinIcon(buffer.ToArray());
    }

    public IWindowIconImpl LoadIcon(IBitmapImpl bitmap)
    {
        using var buffer = new MemoryStream();
        bitmap.Save(buffer, new PngBitmapEncoderOptions());
        return new BasinIcon(buffer.ToArray());
    }

    private sealed class BasinIcon : IWindowIconImpl
    {
        private readonly byte[] _bytes;

        public BasinIcon(byte[] bytes) => _bytes = bytes;

        public void Save(Stream outputStream) => outputStream.Write(_bytes);
    }
}
