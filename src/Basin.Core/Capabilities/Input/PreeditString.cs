namespace Basin.Capabilities;

/// <summary>
/// The preedit text that an input method shows before it commits text.
/// </summary>
/// <param name="Text">The preedit text. An empty string clears the preedit.</param>
/// <param name="CursorBegin">The byte offset in <paramref name="Text"/> where the cursor starts. A negative value hides the cursor.</param>
/// <param name="CursorEnd">The byte offset in <paramref name="Text"/> where the cursor ends.</param>
public readonly record struct PreeditString(string Text, int CursorBegin, int CursorEnd);
