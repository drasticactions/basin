namespace Basin.Capabilities;

/// <summary>
/// Lists the three portal response codes that a dialog can produce.
/// </summary>
public enum PromptResponse
{
    /// <summary>
    /// The user accepted. The portal answers response code 0.
    /// </summary>
    Accepted = 0,

    /// <summary>
    /// The user refused. The portal answers response code 1.
    /// </summary>
    Denied = 1,

    /// <summary>
    /// The user dismissed the dialog or the frontend closed the request. The portal answers response code 2.
    /// </summary>
    Cancelled = 2,
}
