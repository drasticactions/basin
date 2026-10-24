namespace Basin.Transport.Waypipe;

/// <summary>
/// The two ways that a listener can reach the compositor thread. <see cref="WaypipeListeners.BindAsync"/> uses the loop on a host with descriptors and the post delegate on Windows.
/// </summary>
/// <param name="Loop">The compositor loop that watches a listening descriptor. A host with descriptors must supply it.</param>
/// <param name="Post">The delegate that runs an action on the compositor thread. A Windows host must supply it.</param>
public readonly record struct WaypipeListenerHost(ICompositorEventLoop Loop, Action<Action> Post);
