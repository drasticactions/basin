namespace Basin.Capabilities;

/// <summary>
/// Describes one tablet pad that an <see cref="ITabletSource"/> reports.
/// </summary>
/// <param name="Id">The id of the pad. It is unique within the source.</param>
/// <param name="Path">The device node path. It can be empty.</param>
/// <param name="Buttons">The number of buttons on the pad.</param>
/// <param name="Dials">The number of dials on the pad.</param>
public readonly record struct TabletPadInfo(ulong Id, string Path, uint Buttons, uint Dials = 0);
