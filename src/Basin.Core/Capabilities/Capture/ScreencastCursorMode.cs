namespace Basin.Capabilities;

/// <summary>
/// Lists the screencast cursor modes, with the values of the portal <c>cursor_mode</c> option. A combination lists the modes that are available.
/// </summary>
[Flags]
public enum ScreencastCursorMode
{
    /// <summary>
    /// The stream does not show the cursor.
    /// </summary>
    Hidden = 1,

    /// <summary>
    /// The stream draws the cursor into the frames.
    /// </summary>
    Embedded = 2,

    /// <summary>
    /// The stream sends the cursor as metadata next to the frames.
    /// </summary>
    Metadata = 4,
}
