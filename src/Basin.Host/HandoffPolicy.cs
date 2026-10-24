namespace Basin.Host;

/// <summary>
/// Decides whether a compositor adopts the lit DRM state at start and whether it leaves its last frame lit at exit.
/// </summary>
public sealed class HandoffPolicy
{
    /// <summary>
    /// Initializes a new instance of the <see cref="HandoffPolicy"/> class.
    /// </summary>
    /// <param name="mode">The resolved handoff mode.</param>
    /// <param name="greeter">Whether the compositor is a greeter.</param>
    /// <param name="greetd">Whether the greeter runs under greetd.</param>
    public HandoffPolicy(HandoffMode mode, bool greeter = false, bool greetd = false)
    {
        Mode = mode;
        Greeter = greeter;
        Greetd = greetd;
    }

    /// <summary>
    /// Gets the resolved handoff mode.
    /// </summary>
    public HandoffMode Mode { get; }

    /// <summary>
    /// Gets a value indicating whether the compositor is a greeter.
    /// </summary>
    public bool Greeter { get; }

    /// <summary>
    /// Gets a value indicating whether the greeter runs under greetd.
    /// </summary>
    public bool Greetd { get; }

    /// <summary>
    /// Gets a value indicating whether the compositor adopts the lit mode and frame at start. It is <see langword="false"/> only for <see cref="HandoffMode.Off"/>.
    /// </summary>
    public bool Inherits => Mode != HandoffMode.Off;

    /// <summary>
    /// Gets a value indicating whether a greeter adds <c>BASIN_HANDOFF=retain</c> to the environment of the session that it starts. It is <see langword="true"/> for a greeter whose mode is not <see cref="HandoffMode.Off"/>.
    /// </summary>
    public bool StampsSession => Greeter && Mode != HandoffMode.Off;

    /// <summary>
    /// Decides whether a stop leaves the last frame lit. A failure never retains. <see cref="HandoffMode.Retain"/> retains on every other stop. <see cref="HandoffMode.Auto"/> retains only when a greeter under greetd starts a session.
    /// </summary>
    /// <param name="stop">The kind of stop.</param>
    /// <returns><see langword="true"/> when the compositor must retain its last frame.</returns>
    public bool RetainsOn(HandoffStop stop) => stop switch
    {
        HandoffStop.Failure => false,
        _ when Mode == HandoffMode.Retain => true,
        HandoffStop.SessionStart => Mode == HandoffMode.Auto && Greeter && Greetd,
        _ => false,
    };
}
