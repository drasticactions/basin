namespace Basin.Eis;

/// <summary>
/// Holds one pointer barrier of an input capture session, as a line segment in layout coordinates.
/// </summary>
/// <param name="Id">The client's barrier id. It is unique in a session.</param>
/// <param name="X1">The x of the first end.</param>
/// <param name="Y1">The y of the first end.</param>
/// <param name="X2">The x of the second end.</param>
/// <param name="Y2">The y of the second end.</param>
public readonly record struct InputCaptureBarrier(uint Id, int X1, int Y1, int X2, int Y2);
