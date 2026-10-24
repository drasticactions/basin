namespace Basin.Ipc;

/// <summary>
/// The reply of <c>ipc/version</c>.
/// </summary>
/// <param name="Protocol">The version of the control socket protocol, from <see cref="IpcProtocol.Version"/>.</param>
/// <param name="Compositor">The name of the compositor program.</param>
/// <param name="Basin">The version of the basin libraries that the compositor runs.</param>
public readonly record struct IpcVersion(int Protocol, string Compositor, string Basin);
