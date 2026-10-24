namespace Basin.Ipc;

/// <summary>
/// A display mode of an output.
/// </summary>
/// <param name="Width">The width in physical pixels.</param>
/// <param name="Height">The height in physical pixels.</param>
/// <param name="RefreshMhz">The refresh rate in millihertz, for example 60000 for 60 Hz.</param>
public readonly record struct IpcOutputMode(int Width, int Height, int RefreshMhz);
