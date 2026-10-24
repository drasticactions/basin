namespace Basin.Audio.PipeWire;

/// <summary>
/// The step of <see cref="AudioStreamOwners.Match"/> that found the owner of a stream. The steps run in the order SecPid, ReportedPid, Ancestor, Flatpak, LaunchTag.
/// </summary>
public enum AudioMatchStep
{
    /// <summary>
    /// No step matched. The stream has no owner.
    /// </summary>
    None,
    /// <summary>
    /// The launch id of the stream maps to the process group of a window. This step runs last because every child of a launch inherits the tag.
    /// </summary>
    LaunchTag,
    /// <summary>
    /// The verified PID of a native stream equals the PID of a window.
    /// </summary>
    SecPid,
    /// <summary>
    /// The parent chain of the verified PID of a native stream reaches the PID of a window.
    /// </summary>
    Ancestor,
    /// <summary>
    /// The Flatpak application id of the stream equals the Flatpak application of a window process.
    /// </summary>
    Flatpak,
    /// <summary>
    /// The reported PID of a pipewire-pulse stream, or one of its ancestors, equals the PID of a window.
    /// </summary>
    ReportedPid,
}
