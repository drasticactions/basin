using Avalonia;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform;
using Avalonia.Rendering;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Basin.Capabilities;
using Basin.Diagnostics;

namespace Basin.UI.Avalonia;

/// <summary>
/// The settings for <see cref="BasinPlatform.Start{TApp}(BasinPlatformOptions)"/>, <see cref="BasinPlatform.Attach"/> and <see cref="BasinPlatform.UseBasin"/>.
/// </summary>
public sealed class BasinPlatformOptions
{
    /// <summary>
    /// Gets or sets a default scale. The default is 1. The platform does not read this value now.
    /// </summary>
    public double DefaultScale { get; set; } = 1.0;

    /// <summary>
    /// Gets or sets the compositor loop. Clipboard reads need it, and <see cref="AvaloniaUIHost.WakeupRequested"/> is moved onto its thread. Only a started platform reads it.
    /// </summary>
    public ICompositorEventLoop? EventLoop { get; set; }

    /// <summary>
    /// Gets or sets the selection store behind the Avalonia clipboard. Without it the clipboard is empty and writes do nothing.
    /// </summary>
    public ISelectionStore? Selection { get; set; }

    /// <summary>
    /// Gets or sets the source of the screens that Avalonia reports. Without it Avalonia sees one 1920x1080 screen at scale 1.
    /// </summary>
    public IUIScreenSource? Screens { get; set; }

    /// <summary>
    /// Gets or sets the GPU that draws chrome. Without it, surfaces produce memory buffers through software rendering.
    /// </summary>
    public IAvaloniaGpu? Gpu { get; set; }

    /// <summary>
    /// Gets or sets the first theme variant of a started platform. The default is <see cref="UIThemeVariant.Light"/>.
    /// </summary>
    public UIThemeVariant Theme { get; set; } = UIThemeVariant.Light;

    /// <summary>
    /// Gets or sets the thread that owns the surface buffers. <see cref="BasinPlatform.Attach"/> requires it.
    /// </summary>
    public ThreadAffinity? CompositorAffinity { get; set; }

    /// <summary>
    /// Gets or sets a lookup for platform features that <see cref="BasinPlatform.Attach"/> asks before its own. Return <see langword="null"/> for a type that it does not supply.
    /// </summary>
    public Func<Type, object?>? Features { get; set; }

    /// <summary>
    /// Gets or sets a step that <see cref="BasinPlatform.Start{TApp}(BasinPlatformOptions)"/> applies to the app builder after the Basin and Skia setup.
    /// </summary>
    public Func<AppBuilder, AppBuilder>? Configure { get; set; }
}
