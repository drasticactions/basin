using Basin.Capabilities;

namespace Basin.Desktop;

public sealed class GamescopeLimiterModule : IProtocolModule
{
    public string WireInterface => "gamescope_limiter";

    public int Version => GamescopeLimiterManager.Version;

    public FrameLimiter? Limiter { get; private set; }

    public GamescopeLimiterManager? Manager { get; private set; }

    public IDisposable Install(BasinServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        IFrameLimits limits;
        FrameLimiter? owned = null;
        if (services.Find<IFrameLimits>() is { } existing)
        {
            limits = existing;
        }
        else
        {
            owned = new FrameLimiter(services.Loop, services.Require<OutputLayout>());
            services.Use<IFrameLimits>(owned);
            services.Use(owned);
            limits = owned;
        }

        Limiter = owned ?? limits as FrameLimiter;
        Manager = new GamescopeLimiterManager(services.Display, services.Require<CompositorGlobal>(), limits);
        return new Installation(Manager, owned);
    }

    private sealed class Installation(GamescopeLimiterManager manager, FrameLimiter? limiter) : IDisposable
    {
        public void Dispose()
        {
            manager.Dispose();
            limiter?.Dispose();
        }
    }
}
