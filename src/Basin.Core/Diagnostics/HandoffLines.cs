namespace Basin.Diagnostics;

/// <summary>
/// Formats the <c>HANDOFF</c> report lines that tests and scripts match.
/// </summary>
public static class HandoffLines
{
    /// <summary>
    /// Formats the line that reports how an output took its mode from the previous DRM master.
    /// </summary>
    /// <param name="inheritance">The outcome for one output.</param>
    /// <returns>A line that starts with <c>HANDOFF inherit</c>.</returns>
    public static string Inherit(HandoffInheritance inheritance) =>
        FormattableString.Invariant(
            $"HANDOFF inherit {inheritance.Output} mode={inheritance.Mode.Width}x{inheritance.Mode.Height}@{inheritance.Mode.RefreshMilliHz / 1000.0:0.##} ") +
        $"modeset={(inheritance.Modeset ? "yes" : "no")} reason={ReasonName(inheritance.Reason)}" +
        (inheritance.Errno != 0 ? FormattableString.Invariant($" errno={inheritance.Errno}") : string.Empty);

    /// <summary>
    /// Formats the line that reports whether an output kept its last frame for the next DRM master.
    /// </summary>
    /// <param name="retention">The outcome for one output.</param>
    /// <returns>A line that starts with <c>HANDOFF retain</c> or <c>HANDOFF refused</c>.</returns>
    public static string Retain(HandoffRetention retention) =>
        retention.Refusal is { } refusal
            ? $"HANDOFF refused {retention.Output} {refusal}"
            : FormattableString.Invariant($"HANDOFF retain {retention.Output} fb={retention.FramebufferId}");

    /// <summary>
    /// Gets the lowercase word for a handoff reason.
    /// </summary>
    /// <param name="reason">The reason to name.</param>
    /// <returns>The word. An unknown value gives <c>off</c>.</returns>
    public static string ReasonName(HandoffReason reason) => reason switch
    {
        HandoffReason.Matched => "matched",
        HandoffReason.Config => "config",
        HandoffReason.Refused => "refused",
        _ => "off",
    };
}
