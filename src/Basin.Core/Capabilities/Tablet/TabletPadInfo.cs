namespace Basin.Capabilities;

public readonly record struct TabletPadInfo(ulong Id, string Path, uint Buttons, uint Dials = 0);
