namespace Basin.Capabilities;

/// <summary>
/// Sends committed text to the text input of the focused client.
/// </summary>
public interface ITextInputCommit
{
    /// <summary>Gets a value indicating whether the focused client has an enabled text input now.</summary>
    bool HasActiveTextInput { get; }

    /// <summary>Commits text to the active text input, as an input method does.</summary>
    /// <param name="text">The text to insert.</param>
    /// <returns><see langword="false"/> when no text input is active.</returns>
    bool TryCommitString(string text);
}
