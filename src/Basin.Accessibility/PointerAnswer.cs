namespace Basin.Accessibility;

internal readonly record struct PointerAnswer(int Pid, string? AppBusName, string? ToplevelPath, double X, double Y);
