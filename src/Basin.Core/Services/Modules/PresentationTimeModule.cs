using Basin.Capabilities;
using Basin.Diagnostics;
using Wayland;

namespace Basin;

/// <summary>
/// Installs <c>wp_presentation</c>. With an <see cref="OutputLayout"/>, it also adds a feedback pump to the <see cref="IFrameClock"/>.
/// </summary>
public sealed class PresentationTimeModule : IProtocolModule
{
    /// <inheritdoc/>
    public string WireInterface => "wp_presentation";

    /// <inheritdoc/>
    public int Version => PresentationTimeGlobal.Version;

    /// <summary>
    /// Gets the <c>wp_presentation</c> global that <see cref="Install"/> made, or <see langword="null"/> before the install.
    /// </summary>
    public PresentationTimeGlobal? Global { get; private set; }

    /// <inheritdoc/>
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
