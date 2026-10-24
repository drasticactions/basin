namespace Basin.Capabilities;

/// <summary>
/// Follows the keyboard focus so that text inputs enter and leave the correct surface.
/// </summary>
public interface ITextInputFocus : ITextInputCommit
{
    /// <summary>Moves the text input focus to a surface.</summary>
    /// <param name="surface">The surface that has keyboard focus now, or <see langword="null"/> for no focus.</param>
    void NotifyFocus(Surface? surface);
}
