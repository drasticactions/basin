namespace Waylonia;

internal sealed record CaptureHooks(Func<uint, bool, bool> Filter, Action<uint, bool> Inject);
