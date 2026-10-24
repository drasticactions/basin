namespace Basin.Capabilities;

/// <summary>
/// Holds special serial values for <see cref="ISelectionStore.SetSelection"/>.
/// </summary>
public static class SelectionSerial
{
    /// <summary>
    /// A serial that skips the input serial check. Protocols without input serials, for example data-control, use it.
    /// </summary>
    public const uint Unchecked = 0;
}
