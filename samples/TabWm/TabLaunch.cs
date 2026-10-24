namespace TabWm;

internal sealed record TabLaunch(int Pid, string? DesktopId, string Command, string Token, long StartedMs, long AudioTag);
