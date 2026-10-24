namespace Basin;

/// <summary>
/// Reports how a DRM output treated the state that the previous compositor left lit, at its first frame commit. The output raises it once.
/// </summary>
/// <param name="Output">The connector name of the output.</param>
/// <param name="Mode">The mode that the first commit used.</param>
/// <param name="Modeset"><see langword="true"/> when the first commit needed a modeset.</param>
/// <param name="Reason">Why the commit did or did not need a modeset.</param>
/// <param name="Errno">The errno of the refused test commit when <paramref name="Reason"/> is <see cref="HandoffReason.Refused"/>, and zero otherwise.</param>
public readonly record struct HandoffInheritance(
    string Output,
    OutputMode Mode,
    bool Modeset,
    HandoffReason Reason,
    int Errno);
