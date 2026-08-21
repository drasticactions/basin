using Avalonia;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Basin.UI.Avalonia;

internal sealed class BasinCursor : ICursorImpl
{
    public BasinCursor(string name) => Name = name;

    public string Name { get; }

    public void Dispose()
    {
    }
}
