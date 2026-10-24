namespace Basin.Audio.PipeWire;

/// <summary>
/// The kind of device that plays the default sink, as <see cref="AudioOutputWatch.Classify"/> reads it.
/// </summary>
public enum AudioOutputKind
{
    /// <summary>
    /// There is no default sink, or its properties are not read yet.
    /// </summary>
    Unknown,
    /// <summary>
    /// The sink plays through anything that is not headphones. This is the result when nothing names headphones.
    /// </summary>
    Speakers,
    /// <summary>
    /// The form factor, the port type or the route name of the sink names headphones or a headset.
    /// </summary>
    Headphones,
}
