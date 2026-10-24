namespace Basin.Capabilities;

/// <summary>
/// Lists the kinds of controls on a tablet pad.
/// </summary>
public enum TabletPadEventKind
{
    /// <summary>
    /// A pad button.
    /// </summary>
    Button,
    /// <summary>
    /// A touch ring with an absolute position.
    /// </summary>
    Ring,
    /// <summary>
    /// A touch strip with an absolute position.
    /// </summary>
    Strip,
    /// <summary>
    /// A dial that reports relative rotation.
    /// </summary>
    Dial,
}
