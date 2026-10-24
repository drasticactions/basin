using Basin.Capabilities;

namespace Basin.Shell.Xdg;

/// <summary>
/// Installs <c>xdg_wm_base</c>. It registers the <see cref="XdgShell"/> and its <see cref="XdgToplevelSource"/>, and adds the source to the <see cref="AggregateToplevelModel"/>. It makes that model the default <see cref="IToplevelModel"/> when no aggregate model is registered.
/// </summary>
public sealed class XdgShellModule : IProtocolModule
{
    /// <inheritdoc/>
    public string WireInterface => "xdg_wm_base";

    /// <inheritdoc/>
    public int Version => XdgShell.Version;

    /// <summary>
    /// Gets the global that <see cref="Install"/> created. It is <see langword="null"/> before the install.
    /// </summary>
    public XdgShell? Shell { get; private set; }

    /// <summary>
    /// Gets the toplevel source that <see cref="Install"/> created over <see cref="Shell"/>. It is <see langword="null"/> before the install.
    /// </summary>
    public XdgToplevelSource? Toplevels { get; private set; }

    /// <inheritdoc/>
    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Shell = new XdgShell(services.Display, services.Require<CompositorGlobal>(), services.Find<Seat.Seat>());
        Toplevels = new XdgToplevelSource(Shell);
        services.Use(Shell);
        services.Use(Toplevels);

        var model = services.Find<IToplevelModel>() as AggregateToplevelModel;
        if (model is null)
        {
            model = new AggregateToplevelModel();
            services.UseDefault<IToplevelModel>(model);
        }

        model.Add(Toplevels);
        var popups = new XdgPopupDirectory(Shell);
        services.Find<Capabilities.Defaults.PopupDirectory>()?.Add(popups);
        return new ShellHandle(Shell, Toplevels, popups);
    }

    private sealed class ShellHandle : IDisposable
    {
        private readonly XdgShell _shell;
        private readonly XdgToplevelSource _toplevels;
        private readonly XdgPopupDirectory _popups;

        public ShellHandle(XdgShell shell, XdgToplevelSource toplevels, XdgPopupDirectory popups)
        {
            _shell = shell;
            _toplevels = toplevels;
            _popups = popups;
        }

        public void Dispose()
        {
            _popups.Dispose();
            _toplevels.Dispose();
            _shell.Dispose();
        }
    }
}
