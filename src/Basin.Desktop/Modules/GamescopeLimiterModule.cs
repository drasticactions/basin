using Basin.Capabilities;

namespace Basin.Desktop;

/// <summary>
/// Installs the <c>gamescope_limiter</c> global through a <see cref="GamescopeLimiterManager"/>. It uses the registered <see cref="IFrameLimits"/>, or creates and registers its own <see cref="FrameLimiter"/> when none exists.
/// </summary>
public sealed class GamescopeLimiterModule : IProtocolModule
{
    /// <inheritdoc/>
    public string WireInterface => "gamescope_limiter";

    /// <inheritdoc/>
    public int Version => GamescopeLimiterManager.Version;

    /// <summary>
    /// Gets the frame limiter behind the global. It is <see langword="null"/> before the install, and when the registered <see cref="IFrameLimits"/> is not a <see cref="FrameLimiter"/>.
    /// </summary>
    public FrameLimiter? Limiter { get; private set; }

    /// <summary>
    /// Gets the manager that <see cref="Install"/> created. It is <see langword="null"/> before the install.
    /// </summary>
    public GamescopeLimiterManager? Manager { get; private set; }

    /// <summary>
    /// Creates the global. When the module created its own <see cref="FrameLimiter"/>, disposing the returned handle disposes that limiter too.
    /// </summary>
    /// <param name="services">The registry to read capabilities from and to add the limiter to.</param>
    /// <returns>A handle that removes the global.</returns>
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
