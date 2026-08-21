using Basin.Capabilities;

namespace Basin.Shell.Xdg;

public sealed class XdgShellModule : IProtocolModule
{
    public string WireInterface => "xdg_wm_base";

    public int Version => XdgShell.Version;

    public XdgShell? Shell { get; private set; }

    public XdgToplevelSource? Toplevels { get; private set; }

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
        return new ShellHandle(Shell, Toplevels);
    }

    private sealed class ShellHandle : IDisposable
    {
        private readonly XdgShell _shell;
        private readonly XdgToplevelSource _toplevels;

        public ShellHandle(XdgShell shell, XdgToplevelSource toplevels)
        {
            _shell = shell;
            _toplevels = toplevels;
        }

        public void Dispose()
        {
            _toplevels.Dispose();
            _shell.Dispose();
        }
    }
}
