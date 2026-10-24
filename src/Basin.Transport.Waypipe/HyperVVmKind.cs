namespace Basin.Transport.Waypipe;

/// <summary>
/// Tells how a <see cref="HyperVVm"/> gets the VM id that a Hyper-V listener binds.
/// </summary>
public enum HyperVVmKind
{
    /// <summary>
    /// The VM id of the running WSL2 VM, which the listener resolves at bind and again after the VM restarts.
    /// </summary>
    Wsl,

    /// <summary>
    /// A fixed VM id, either a literal GUID or one of the Hyper-V wildcard ids.
    /// </summary>
    Fixed,
}
