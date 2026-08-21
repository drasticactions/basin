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

public static class BasinPlatform
{
    private static AvaloniaUIHost? _host;
    private static bool _started;

    public static AvaloniaUIHost Host =>
        _host ?? throw new InvalidOperationException("The Avalonia platform is not started.");

    public static bool IsStarted => _started;

    public static AvaloniaUIHost Start<TApp>(BasinPlatformOptions options)
        where TApp : Application, new()
    {
        ArgumentNullException.ThrowIfNull(options);

        ClaimStart();
        AppBuilder.Configure<TApp>()
            .UseBasin(options)
            .UseSkia()
            .SetupWithoutStarting();
        return Host;
    }

    public static AppBuilder UseBasin(this AppBuilder builder, BasinPlatformOptions? options = null) =>
        builder
            .UseStandardRuntimePlatformSubsystem()
            .UseWindowingSubsystem(() => Initialize(options ?? new BasinPlatformOptions()), "Basin")
            .UseHarfBuzz();

    internal static void ClaimStart()
    {
        if (_started)
        {
            throw new InvalidOperationException(
                "The Avalonia platform is already started in this process. " +
                "Avalonia installs its UI thread dispatcher once per process and BasinPlatform.Host " +
                "is one host, so a second compositor must run as a second process.");
        }

        _started = true;
    }

    internal static void Initialize(BasinPlatformOptions options)
    {
        var dispatcher = new BasinDispatcherImpl();
        Dispatcher.InitializeUIThreadDispatcher(dispatcher);

        var renderTimer = new BasinRenderTimer();
        var screens = new BasinScreens(options.Screens);
        var settings = new BasinPlatformSettings { Variant = options.Theme };
        var clipboardImpl = new BasinClipboardImpl(options.Selection, options.EventLoop);
        var clipboard = new BasinClipboard(clipboardImpl);

        AvaloniaLocator.CurrentMutable
            .Bind<IRenderTimer>().ToConstant(renderTimer)
            .Bind<IRenderLoop>().ToConstant(RenderLoop.FromTimer(renderTimer))
            .Bind<IKeyboardDevice>().ToConstant(new KeyboardDevice())
            .Bind<ICursorFactory>().ToConstant(new BasinCursorFactory())
            .Bind<IPlatformIconLoader>().ToConstant(new BasinIconLoader())
            .Bind<IClipboardImpl>().ToConstant(clipboardImpl)
            .Bind<IClipboard>().ToConstant(clipboard)
            .Bind<IScreenImpl>().ToConstant(screens)
            .Bind<IPlatformSettings>().ToConstant(settings)
            .Bind<PlatformHotkeyConfiguration>().ToConstant(new PlatformHotkeyConfiguration(KeyModifiers.Control))
            .Bind<KeyGestureFormatInfo>().ToConstant(new KeyGestureFormatInfo(meta: "Super"));

        var compositor = new Compositor(null, useUiThreadForSynchronousCommits: true);
        var context = new BasinPlatformContext(compositor, TryGetFeature) { Screens = screens };
        AvaloniaLocator.CurrentMutable.Bind<IWindowingPlatform>().ToConstant(new BasinWindowingPlatform(context));

        _host = new AvaloniaUIHost(dispatcher, renderTimer, context) { Settings = settings };

        object? TryGetFeature(Type featureType)
        {
            if (featureType == typeof(IClipboard))
            {
                return clipboard;
            }

            return featureType == typeof(IScreenImpl) ? screens : null;
        }
    }
}
