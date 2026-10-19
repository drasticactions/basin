using Basin.Capabilities;
using Basin.Scene;

namespace Basin.Host;

public sealed class HostSceneDiagnostics(BasinHost host, BasinServices services) : ISceneDiagnostics
{
    private SceneDiagnostics? _inner;

    public int ClockSinks => Inner?.ClockSinks ?? -1;

    private SceneDiagnostics? Inner =>
        _inner ??= host.Driver is { } driver
            ? new SceneDiagnostics(driver.Scene, driver.Renderer, host.Loop, driver) { Services = services }
            : null;

    public IReadOnlyList<SceneNodeInfo> Tree() => Inner?.Tree() ?? [];

    public IReadOnlyList<FrameInfo> Frames() => Inner?.Frames() ?? [];

    public IDisposable WatchDamage(bool stacks, Action<DamageInfo> sink) => Require().WatchDamage(stacks, sink);

    public IDisposable WatchFrames(Action<FrameInfo> sink) => Require().WatchFrames(sink);

    public Func<Box?>? LocateWindow(Surface surface, int margin) => Inner?.LocateWindow(surface, margin);

    public Func<Box?>? LocateSurface(Surface surface, int margin) => Inner?.LocateSurface(surface, margin);

    public Func<Box?>? LocateNode(string path, int margin, out string? error)
    {
        if (Inner is { } inner)
        {
            return inner.LocateNode(path, margin, out error);
        }

        error = "the compositor has no output driver yet";
        return null;
    }

    public string? Record(RecordRequest request, Action<RecordingResult> done) =>
        Inner is { } inner ? inner.Record(request, done) : "the compositor has no output driver yet";

    public bool WriteParts(Surface surface, Action<FramePart, Box> part) => Inner?.WriteParts(surface, part) ?? false;

    public void AllowPlanes(bool allowed) => Inner?.AllowPlanes(allowed);

    public IReadOnlyList<PlaneReport> Planes() => Inner?.Planes() ?? [];

    public IDisposable WatchPlanes(Action<PlaneReport> sink) => Require().WatchPlanes(sink);

    private SceneDiagnostics Require() =>
        Inner ?? throw new InvalidOperationException("the compositor has no output driver yet");
}
