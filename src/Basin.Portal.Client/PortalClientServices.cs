using Basin.Capabilities;
using Basin.Eis;
using Wayland;

namespace Basin.Portal.Client;

/// <summary>
/// Builds the capabilities that a portal running as a Wayland client gives to the portal modules, from the globals that the compositor offers.
/// It owns every service that it creates.
/// </summary>
public sealed class PortalClientServices : IDisposable
{
    private readonly ClientGlobals _globals;
    private readonly ClientOutputs _outputs;
    private readonly ClientKeymap _keymap;
    private readonly List<IDisposable> _owned = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="PortalClientServices"/> class and creates the toplevel list and the capture client when their globals are bound.
    /// </summary>
    /// <param name="globals">The bound globals. Bind them before this call.</param>
    /// <param name="outputs">The outputs of the compositor.</param>
    /// <param name="keymap">The keymap of the seat keyboard.</param>
    /// <param name="display">The display connection.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public PortalClientServices(ClientGlobals globals, ClientOutputs outputs, ClientKeymap keymap, WlDisplay display)
    {
        ArgumentNullException.ThrowIfNull(globals);
        ArgumentNullException.ThrowIfNull(outputs);
        ArgumentNullException.ThrowIfNull(keymap);
        ArgumentNullException.ThrowIfNull(display);
        _globals = globals;
        _outputs = outputs;
        _keymap = keymap;
        if (globals.ToplevelList is { } list)
        {
            Toplevels = new ForeignToplevelList(list);
            _owned.Add(Toplevels);
        }

        if (globals.Capture is { } capture && globals.Shm is { } shm)
        {
            Capture = new ImageCopyCaptureClient(capture, globals.OutputSources, globals.ToplevelSources, shm, globals.Dmabuf, outputs, Toplevels, display);
            _owned.Add(Capture);
        }
    }

    /// <summary>
    /// Gets the toplevel list, or <see langword="null"/> when the compositor has no foreign toplevel list.
    /// </summary>
    public ForeignToplevelList? Toplevels { get; }

    /// <summary>
    /// Gets the capture client, or <see langword="null"/> when the compositor has no image copy capture or no shared memory.
    /// </summary>
    public ImageCopyCaptureClient? Capture { get; }

    /// <summary>
    /// Registers the capabilities in a service table and removes each portal interface whose globals are missing from the pack.
    /// Call it once, before the table freezes.
    /// </summary>
    /// <param name="services">The service table to fill.</param>
    /// <param name="pack">The portal pack to narrow.</param>
    /// <param name="screencast">A factory for the screencast publisher, or <see langword="null"/> to remove the ScreenCast portal.</param>
    /// <returns>The pack without the portal interfaces that cannot work.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="pack"/> is <see langword="null"/>.</exception>
    public ProtocolPack Register(
        BasinServices services,
        ProtocolPack pack,
        Func<ImageCopyCaptureClient, IScreencastPublisher?>? screencast = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(pack);
        services.Use<IActiveKeymap>(_keymap);
        services.Use<IKeymapLookup>(_keymap);
        if (Toplevels is not null)
        {
            services.Use<IToplevelModel>(Toplevels);
        }

        if (Capture is not null)
        {
            services.Use<IScreenCapture>(Capture);
        }

        if (Capture is not { IsAvailable: true })
        {
            pack = pack.Without("org.freedesktop.impl.portal.Screenshot");
        }

        if (!RegisterScreencast(services, screencast))
        {
            pack = pack.Without("org.freedesktop.impl.portal.ScreenCast");
        }

        if (_globals.VirtualPointers is null && _globals.VirtualKeyboards is null)
        {
            pack = pack.Without("org.freedesktop.impl.portal.RemoteDesktop");
        }
        else
        {
            services.Use<IInputSink>(Own(new VirtualInputInjector(_globals.VirtualKeyboards, _globals.VirtualPointers, _globals.Seat, _keymap)));
        }

        if (_globals.DataControl is { } dataControl && _globals.Seat is { } seat)
        {
            services.Use<ISelectionStore>(Own(new DataControlSelectionStore(dataControl, seat)));
        }
        else
        {
            pack = pack.Without("org.freedesktop.impl.portal.Clipboard");
        }

        if (_globals.GlobalShortcuts is { } shortcuts)
        {
            services.Use<IGlobalShortcuts>(Own(new HyprlandShortcutRegistry(shortcuts)));
        }
        else
        {
            pack = pack.Without("org.freedesktop.impl.portal.GlobalShortcuts");
        }

        if (_globals.InputCapture is { } inputCapture && EisLibrary.IsAvailable(out _))
        {
            services.Use<IInputCaptureProvider>(Own(new HyprlandInputCaptureProvider(inputCapture, _outputs)));
        }
        else
        {
            pack = pack.Without("org.freedesktop.impl.portal.InputCapture");
        }

        return pack;
    }

    /// <summary>
    /// Disposes every owned service in the reverse order of creation.
    /// </summary>
    public void Dispose()
    {
        for (var i = _owned.Count - 1; i >= 0; i--)
        {
            _owned[i].Dispose();
        }

        _owned.Clear();
    }

    private bool RegisterScreencast(BasinServices services, Func<ImageCopyCaptureClient, IScreencastPublisher?>? screencast)
    {
        if (Capture is not { } capture || screencast is null)
        {
            return false;
        }

        capture.ProbeDmabuf();
        if (screencast(capture) is not { } publisher)
        {
            return false;
        }

        if (publisher is IDisposable disposable)
        {
            _owned.Add(disposable);
        }

        services.Use<IScreencastPublisher>(publisher);
        return capture.IsAvailable;
    }

    private T Own<T>(T owned)
        where T : IDisposable
    {
        _owned.Add(owned);
        return owned;
    }
}
