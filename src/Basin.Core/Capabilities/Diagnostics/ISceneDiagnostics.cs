namespace Basin.Capabilities;

public interface ISceneDiagnostics
{
    IReadOnlyList<SceneNodeInfo> Tree();

    IReadOnlyList<FrameInfo> Frames();

    int ClockSinks { get; }

    IDisposable WatchDamage(bool stacks, Action<DamageInfo> sink);

    IDisposable WatchFrames(Action<FrameInfo> sink);

    Func<Box?>? LocateWindow(Surface surface, int margin);

    Func<Box?>? LocateSurface(Surface surface, int margin);

    Func<Box?>? LocateNode(string path, int margin, out string? error);

    string? Record(RecordRequest request, Action<RecordingResult> done);

    bool WriteParts(Surface surface, Action<FramePart, Box> part);

    void AllowPlanes(bool allowed);

    IReadOnlyList<PlaneReport> Planes();

    IDisposable WatchPlanes(Action<PlaneReport> sink);
}
