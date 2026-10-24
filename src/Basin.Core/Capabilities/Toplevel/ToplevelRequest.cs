namespace Basin.Capabilities;

/// <summary>
/// A request that <see cref="IToplevelModel.Request"/> sends to the owner of a toplevel.
/// </summary>
/// <param name="Kind">The action.</param>
/// <param name="Output">The target output for <see cref="ToplevelRequestKind.SendToOutput"/>, or <see langword="null"/>.</param>
/// <param name="Surface">The surface that the box of <see cref="ToplevelRequestKind.SetMinimizedGeometry"/> is relative to, or <see langword="null"/>.</param>
/// <param name="Geometry">The box for <see cref="ToplevelRequestKind.SetMinimizedGeometry"/>. For <see cref="ToplevelRequestKind.Move"/> its position, and for <see cref="ToplevelRequestKind.Resize"/> its size.</param>
public readonly record struct ToplevelRequest(
    ToplevelRequestKind Kind,
    IOutput? Output = null,
    Surface? Surface = null,
    Box Geometry = default);
