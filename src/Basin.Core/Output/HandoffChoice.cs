namespace Basin;

/// <summary>
/// Holds the two sources of a DRM handoff mode that come from outside the configuration: the command-line flag and the <c>BASIN_HANDOFF</c> environment variable.
/// </summary>
/// <param name="Flag">The mode from the <c>--handoff</c> flag, or <see langword="null"/> when the flag is absent.</param>
/// <param name="Environment">The mode from <c>BASIN_HANDOFF</c>, or <see langword="null"/> when the variable is absent or not valid.</param>
public readonly record struct HandoffChoice(HandoffMode? Flag, HandoffMode? Environment)
{
    /// <summary>
    /// The name of the environment variable that a greeter sets for the session compositor it starts.
    /// </summary>
    public const string Variable = "BASIN_HANDOFF";

    /// <summary>
    /// Reads <c>BASIN_HANDOFF</c> and removes it from the process environment, so child processes and nested compositors do not inherit it. Call it once at startup.
    /// </summary>
    /// <param name="flag">The mode from the command line, or <see langword="null"/>.</param>
    /// <returns>The flag and the environment value together.</returns>
    public static HandoffChoice Take(HandoffMode? flag)
    {
        var value = System.Environment.GetEnvironmentVariable(Variable);
        System.Environment.SetEnvironmentVariable(Variable, null);
        return new HandoffChoice(flag, Parse(value));
    }

    /// <summary>
    /// Parses the text form of a handoff mode: <c>auto</c>, <c>retain</c> or <c>off</c>. The match is case-sensitive.
    /// </summary>
    /// <param name="value">The text to parse.</param>
    /// <returns>The mode, or <see langword="null"/> for any other text.</returns>
    public static HandoffMode? Parse(string? value) => value switch
    {
        "auto" => HandoffMode.Auto,
        "retain" => HandoffMode.Retain,
        "off" => HandoffMode.Off,
        _ => null,
    };

    /// <summary>
    /// Gets the text form of a handoff mode, which <see cref="Parse"/> reads back.
    /// </summary>
    /// <param name="mode">The mode to name.</param>
    /// <returns><c>retain</c>, <c>off</c> or <c>auto</c>.</returns>
    public static string Name(HandoffMode mode) => mode switch
    {
        HandoffMode.Retain => "retain",
        HandoffMode.Off => "off",
        _ => "auto",
    };

    /// <summary>
    /// Resolves the mode in force. The flag wins, then a configured mode other than <see cref="HandoffMode.Auto"/>, then the environment, then <see cref="HandoffMode.Auto"/>.
    /// </summary>
    /// <param name="configured">The mode from the compositor's configuration file.</param>
    /// <returns>The mode that the compositor must use.</returns>
    public HandoffMode Resolve(HandoffMode configured) =>
        Flag ?? (configured != HandoffMode.Auto ? configured : Environment ?? HandoffMode.Auto);
}
