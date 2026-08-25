using Avalonia;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform;
using Avalonia.Rendering;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Basin.Capabilities;

namespace Basin.UI.Avalonia;

public sealed class BasinPlatformOptions
{
    public double DefaultScale { get; set; } = 1.0;

    public ICompositorEventLoop? EventLoop { get; set; }

    public ISelectionStore? Selection { get; set; }

    public IUIScreenSource? Screens { get; set; }

    public IAvaloniaGpu? Gpu { get; set; }

    public UIThemeVariant Theme { get; set; } = UIThemeVariant.Light;
}
