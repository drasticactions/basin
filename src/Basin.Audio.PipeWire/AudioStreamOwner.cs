namespace Basin.Audio.PipeWire;

/// <summary>
/// The result of <see cref="AudioStreamOwners.Match"/>: the window that owns a stream and the step that found it. The default value means no match.
/// </summary>
/// <param name="ToplevelId">The id of the owning toplevel in the <see cref="Basin.Capabilities.IToplevelModel"/> id space. It is 0 when nothing matched.</param>
/// <param name="Via">The step that matched, or <see cref="AudioMatchStep.None"/>.</param>
public readonly record struct AudioStreamOwner(ulong ToplevelId, AudioMatchStep Via)
{
    /// <summary>
    /// Gets a value indicating whether a step found an owner.
    /// </summary>
    public bool Matched => Via != AudioMatchStep.None;

    /// <summary>
    /// Gets the step as a lowercase name for a log line, for example <c>sec-pid</c>, or <c>none</c>.
    /// </summary>
    public string ViaName => Via switch
    {
        AudioMatchStep.LaunchTag => "launch-tag",
        AudioMatchStep.SecPid => "sec-pid",
        AudioMatchStep.Ancestor => "ancestor",
        AudioMatchStep.Flatpak => "flatpak",
        AudioMatchStep.ReportedPid => "reported-pid",
        _ => "none",
    };
}
