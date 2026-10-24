using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Platform;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform;
using Avalonia.Rendering;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Basin.Capabilities;

namespace Basin.UI.Avalonia;

/// <summary>
/// Starts Avalonia on the Basin windowing platform, so that its windows become compositor chrome surfaces.
/// Avalonia allows one UI dispatcher per process, so a process can start the platform once.
/// </summary>
public static class BasinPlatform
{
    private static AvaloniaUIHost? _host;
    private static bool _started;

    /// <summary>
    /// Gets the host that the started platform made.
    /// </summary>
    /// <exception cref="InvalidOperationException">The platform is not started.</exception>
    public static AvaloniaUIHost Host =>
        _host ?? throw new InvalidOperationException("The Avalonia platform is not started.");

    /// <summary>
    /// Gets a value indicating whether <see cref="Start{TApp}(BasinPlatformOptions, IApplicationLifetime?)"/> has run in this process.
    /// </summary>
    public static bool IsStarted => _started;

    /// <summary>
    /// Sets up Avalonia on the Basin platform with no lifetime. The calling thread becomes the Avalonia UI thread.
    /// </summary>
    /// <typeparam name="TApp">The Avalonia application type.</typeparam>
    /// <param name="options">The platform settings.</param>
    /// <returns>The host that pumps Avalonia and makes chrome surfaces.</returns>
    /// <exception cref="InvalidOperationException">The platform is already started in this process.</exception>
    public static AvaloniaUIHost Start<TApp>(BasinPlatformOptions options)
        where TApp : Application, new() =>
        Start<TApp>(options, null);

    /// <summary>
    /// Sets up Avalonia on the Basin platform with Skia. The calling thread becomes the Avalonia UI thread.
    /// </summary>
    /// <typeparam name="TApp">The Avalonia application type.</typeparam>
    /// <param name="options">The platform settings.</param>
    /// <param name="lifetime">The application lifetime, or <see langword="null"/> to set up without one.</param>
    /// <returns>The host that pumps Avalonia and makes chrome surfaces.</returns>
    /// <exception cref="InvalidOperationException">The platform is already started in this process.</exception>
    public static AvaloniaUIHost Start<TApp>(BasinPlatformOptions options, IApplicationLifetime? lifetime)
        where TApp : Application, new()
    {
        ArgumentNullException.ThrowIfNull(options);

        ClaimStart();
        var builder = AppBuilder.Configure<TApp>()
            .UseBasin(options)
            .UseSkia();
        if (options.Configure is { } configure)
        {
            builder = configure(builder);
        }

        if (lifetime is null)
        {
            builder.SetupWithoutStarting();
        }
        else
        {
            builder.SetupWithLifetime(lifetime);
        }

        return Host;
    }

    /// <summary>
    /// Makes a chrome host inside an Avalonia application that already runs on another platform. The host does not pump Avalonia.
    /// </summary>
    /// <param name="options">The settings. <see cref="BasinPlatformOptions.CompositorAffinity"/> must be set.</param>
    /// <returns>A new host. The caller owns it and must dispose it.</returns>
    /// <exception cref="ArgumentException"><see cref="BasinPlatformOptions.CompositorAffinity"/> is <see langword="null"/>.</exception>
    public static AvaloniaUIHost Attach(BasinPlatformOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.CompositorAffinity is null)
        {
            throw new ArgumentException(
                "an attached host needs CompositorAffinity, the thread that owns the surfaces' buffers.", nameof(options));
        }

        var screens = new BasinScreens(options.Screens);
        var consumerFeatures = options.Features;
        var compositor = new Compositor(options.Gpu?.Graphics, useUiThreadForSynchronousCommits: true);
        var context = new BasinPlatformContext(compositor, TryGetFeature)
        {
            Gpu = options.Gpu,
            Screens = screens,
            Affinity = options.CompositorAffinity,
        };
        return new AvaloniaUIHost(context);

        object? TryGetFeature(Type featureType)
        {
            if (consumerFeatures?.Invoke(featureType) is { } feature)
            {
                return feature;
            }

            return featureType == typeof(IScreenImpl) ? screens : null;
        }
    }

    /// <summary>
    /// Selects the Basin windowing subsystem and HarfBuzz text shaping on an app builder. It does not mark the platform as started.
    /// </summary>
    /// <param name="builder">The app builder.</param>
    /// <param name="options">The platform settings, or <see langword="null"/> for the defaults.</param>
    /// <returns>The same builder.</returns>
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

        var compositor = new Compositor(options.Gpu?.Graphics, useUiThreadForSynchronousCommits: true);
        var context = new BasinPlatformContext(compositor, TryGetFeature) { Gpu = options.Gpu, Screens = screens };
        AvaloniaLocator.CurrentMutable.Bind<IWindowingPlatform>().ToConstant(new BasinWindowingPlatform(context));

        _host = new AvaloniaUIHost(dispatcher, renderTimer, context)
        {
            Settings = settings,
            Wake = options.EventLoop is { } loop ? new CompositorSynchronizationContext(loop) : null,
        };

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
