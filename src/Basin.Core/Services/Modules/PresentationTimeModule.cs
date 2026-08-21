using Basin.Capabilities;
using Basin.Diagnostics;
using Wayland;

namespace Basin;

public sealed class PresentationTimeModule : IProtocolModule
{
    public string WireInterface => "wp_presentation";

    public int Version => PresentationTimeGlobal.Version;

    public PresentationTimeGlobal? Global { get; private set; }

    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        Global = new PresentationTimeGlobal(services.Display, services.Require<CompositorGlobal>());
        services.Use(Global);
        if (services.Find<OutputLayout>() is not { } layout)
        {
            return Global;
        }

        var pump = new PresentationFeedbackPump(Global, layout);
        var clock = services.Find<IFrameClock>();
        clock?.Add(pump);
        return new PresentationInstallation(Global, pump, clock);
    }

    private sealed class PresentationInstallation(
        PresentationTimeGlobal global,
        PresentationFeedbackPump pump,
        IFrameClock? clock)
        : IDisposable
    {
        public void Dispose()
        {
            clock?.Remove(pump);
            pump.Dispose();
            global.Dispose();
        }
    }
}
