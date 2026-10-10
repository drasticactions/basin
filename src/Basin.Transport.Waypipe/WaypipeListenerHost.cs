namespace Basin.Transport.Waypipe;

public readonly record struct WaypipeListenerHost(ICompositorEventLoop Loop, Action<Action> Post);
