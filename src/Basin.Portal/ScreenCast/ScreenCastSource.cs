namespace Basin.Portal;

public readonly record struct ScreenCastSource(
    ScreenCastSourceKind Kind,
    IOutput? Output,
    ulong ToplevelId,
    string OutputName = "",
    string AppId = "",
    string Title = "");
