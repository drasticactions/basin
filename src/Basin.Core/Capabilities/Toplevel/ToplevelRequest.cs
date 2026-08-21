namespace Basin.Capabilities;

public readonly record struct ToplevelRequest(ToplevelRequestKind Kind, IOutput? Output = null);
