namespace Basin.Capabilities;

/// <summary>
/// The number of live protocol objects that a client holds for one interface at one version.
/// </summary>
/// <param name="Interface">The wire name of the interface, for example <c>wl_surface</c>.</param>
/// <param name="Version">The version that the objects were bound or created at.</param>
/// <param name="Count">The number of live objects with this interface and version.</param>
public readonly record struct ClientObjectInfo(string Interface, uint Version, int Count);
