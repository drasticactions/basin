namespace Basin.Diagnostics;

public static class HandoffLines
{
    public static string Inherit(HandoffInheritance inheritance) =>
        FormattableString.Invariant(
            $"HANDOFF inherit {inheritance.Output} mode={inheritance.Mode.Width}x{inheritance.Mode.Height}@{inheritance.Mode.RefreshMilliHz / 1000.0:0.##} ") +
        $"modeset={(inheritance.Modeset ? "yes" : "no")} reason={ReasonName(inheritance.Reason)}" +
        (inheritance.Errno != 0 ? FormattableString.Invariant($" errno={inheritance.Errno}") : string.Empty);

    public static string Retain(HandoffRetention retention) =>
        retention.Refusal is { } refusal
            ? $"HANDOFF refused {retention.Output} {refusal}"
            : FormattableString.Invariant($"HANDOFF retain {retention.Output} fb={retention.FramebufferId}");

    public static string ReasonName(HandoffReason reason) => reason switch
    {
        HandoffReason.Matched => "matched",
        HandoffReason.Config => "config",
        HandoffReason.Refused => "refused",
        _ => "off",
    };
}
