using Avalonia.Platform;

namespace Basin.UI.Avalonia;

public interface IAvaloniaGpu
{
    IPlatformGraphics Graphics { get; }

    IAvaloniaGpuTarget CreateTarget();
}
