namespace Basin.Ipc;

/// <summary>
/// The data of the <c>output/power</c> event, which goes out when an output turns on or off.
/// </summary>
/// <param name="Name">The output name.</param>
/// <param name="On"><see langword="true"/> when the output is now on.</param>
public readonly record struct IpcOutputPowerChanged(string Name, bool On);
