namespace Basin.Ipc;

/// <summary>
/// One output in the reply of <c>outputs/list</c>.
/// </summary>
/// <param name="Name">The output name, for example <c>DP-1</c>. A method names an output by it, because the name survives hotplug.</param>
/// <param name="Description">The human-readable description of the output.</param>
/// <param name="Make">The manufacturer name.</param>
/// <param name="Model">The model name.</param>
/// <param name="Serial">The serial number, or an empty string when the output reports none.</param>
/// <param name="Enabled"><see langword="true"/> when the output is on.</param>
/// <param name="Mode">The current mode.</param>
/// <param name="Refresh">The refresh rate of the current mode in hertz.</param>
/// <param name="Scale">The scale factor from logical to physical pixels.</param>
/// <param name="Transform">The transform name: <c>normal</c>, <c>90</c>, <c>180</c>, <c>270</c>, <c>flipped</c> or <c>flipped-90</c> to <c>flipped-270</c>.</param>
/// <param name="AdaptiveSync"><see langword="true"/> when adaptive sync is on.</param>
/// <param name="PhysicalSize">The physical size of the panel in millimeters, or zeros when it is unknown.</param>
/// <param name="Geometry">The box of the output in layout coordinates. It is JSON <see langword="null"/> when the output is not in the layout.</param>
/// <param name="Power">The power state, or <see langword="null"/> when the session has no output power control.</param>
/// <param name="Pointer"><see langword="true"/> when the pointer is on this output.</param>
public readonly record struct IpcOutput(
    string Name,
    string Description,
    string Make,
    string Model,
    string Serial,
    bool Enabled,
    IpcOutputMode Mode,
    double Refresh,
    double Scale,
    string Transform,
    bool AdaptiveSync,
    IpcSize PhysicalSize,
    IpcOptionalBox Geometry,
    bool? Power,
    bool Pointer);
