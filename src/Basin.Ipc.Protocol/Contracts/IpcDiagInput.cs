using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The data of the <c>diag/input</c> event: one input event that the seat delivered.
/// </summary>
/// <param name="Kind"><c>enter</c>, <c>leave</c>, <c>motion</c>, <c>button</c>, <c>axis</c>, <c>grab-start</c>, <c>grab-end</c> or <c>keyboard-focus</c>.</param>
/// <param name="Window">The id of the window of the event surface, or 0 when the surface is not a window.</param>
/// <param name="AppId">The app id of that window, or <see langword="null"/>.</param>
/// <param name="X">The pointer x in the coordinates of the pointer focus.</param>
/// <param name="Y">The pointer y in the coordinates of the pointer focus.</param>
/// <param name="LayoutX">The pointer x in layout coordinates.</param>
/// <param name="LayoutY">The pointer y in layout coordinates.</param>
/// <param name="Code">The evdev button code for <c>button</c>, or the axis for <c>axis</c>. Otherwise 0.</param>
/// <param name="Pressed">For <c>button</c>, whether the button went down.</param>
/// <param name="Value">For <c>axis</c>, the axis value.</param>
/// <param name="Detail">The axis source for <c>axis</c>, the grab type for a grab event, or <see langword="null"/>.</param>
public readonly record struct IpcDiagInput(
    string Kind,
    ulong Window,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AppId,
    double X,
    double Y,
    double LayoutX,
    double LayoutY,
    uint Code,
    bool Pressed,
    double Value,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Detail);
