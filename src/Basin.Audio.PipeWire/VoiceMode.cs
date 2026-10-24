namespace Basin.Audio.PipeWire;

/// <summary>
/// Selects the filter graph that a <see cref="VoiceGraph"/> builds for a <see cref="SpatialVoice"/>.
/// </summary>
public enum VoiceMode
{
    /// <summary>
    /// A constant-power stereo pan from the azimuth. Elevation and radius have no effect.
    /// </summary>
    Pan,
    /// <summary>
    /// The spatializer of the sofa plugin with an HRTF file. It uses azimuth, elevation and radius.
    /// </summary>
    Hrtf,
    /// <summary>
    /// The two input channels pass through unchanged. Only the reverb level is a control. Use it for sound that is already placed.
    /// </summary>
    Stereo,
}
