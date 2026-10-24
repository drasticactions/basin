namespace Basin.Portal;

/// <summary>
/// A source to cast: an output or a window. A source from restore data has only the names set.
/// </summary>
/// <param name="Kind">Whether the source is an output or a window.</param>
/// <param name="Output">The output for a live monitor source, otherwise <see langword="null"/>.</param>
/// <param name="ToplevelId">The toplevel id for a live window source, otherwise 0.</param>
/// <param name="OutputName">The output name, for restore data.</param>
/// <param name="AppId">The window app id, for restore data.</param>
/// <param name="Title">The window title, for restore data.</param>
public readonly record struct ScreenCastSource(
    ScreenCastSourceKind Kind,
    IOutput? Output,
    ulong ToplevelId,
    string OutputName = "",
    string AppId = "",
    string Title = "");
