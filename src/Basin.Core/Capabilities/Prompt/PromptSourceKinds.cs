namespace Basin.Capabilities;

/// <summary>
/// Lists the kinds of screencast sources. A combination of flags offers or selects more than one kind.
/// </summary>
[Flags]
public enum PromptSourceKinds
{
    /// <summary>
    /// No source kind.
    /// </summary>
    None = 0,

    /// <summary>
    /// A whole output.
    /// </summary>
    Monitor = 1,

    /// <summary>
    /// One toplevel window.
    /// </summary>
    Window = 2,

    /// <summary>
    /// A virtual output that exists only for the stream.
    /// </summary>
    Virtual = 4,
}
