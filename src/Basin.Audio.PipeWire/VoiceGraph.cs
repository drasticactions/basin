using System.Globalization;
using System.Text;

namespace Basin.Audio.PipeWire;

/// <summary>
/// Describes the filter-chain graph of a <see cref="SpatialVoice"/>. A file is not a control, so a new file needs a new voice.
/// </summary>
/// <param name="Mode">The kind of placement that the graph applies.</param>
/// <param name="HrtfFile">The SOFA file for <see cref="VoiceMode.Hrtf"/>. <see cref="SpatialVoice.Create"/> fails when that mode has no existing file.</param>
/// <param name="ImpulseFile">The stereo impulse WAV for the reverb, for example from <see cref="RoomImpulse.PathFor"/>, or <see langword="null"/> for no reverb.</param>
public sealed record VoiceGraph(VoiceMode Mode, string? HrtfFile = null, string? ImpulseFile = null)
{
    /// <summary>
    /// Gets the smallest radius in meters that the HRTF file measures. The voice clamps <see cref="VoiceParams.Radius"/> to this range.
    /// </summary>
    public float HrtfRadiusMin { get; init; } = 1;

    /// <summary>
    /// Gets the largest radius in meters that the HRTF file measures. <see cref="SofaFile.TryProbe"/> reads the range.
    /// </summary>
    public float HrtfRadiusMax { get; init; } = 1;

    /// <summary>
    /// Gets a value indicating whether the graph has a convolver path, which is when <see cref="ImpulseFile"/> is set.
    /// </summary>
    public bool Reverb => ImpulseFile is not null;

    internal string Arguments(string name, string remote, string description)
    {
        var text = new StringBuilder();
        text.Append("{ remote.name = ").Append(Quote(remote));
        text.Append(" node.description = ").Append(Quote(description));
        text.Append(" media.name = ").Append(Quote(description));
        text.Append(" filter.graph = { nodes = [");
        if (Mode == VoiceMode.Stereo)
        {
            AppendStereo(text);
        }
        else
        {
            AppendPlaced(text);
        }

        text.Append(" capture.props = { node.name = ").Append(Quote(name));
        text.Append(" media.class = Audio/Sink audio.position = [ FL FR ] node.virtual = true priority.session = 0 priority.driver = 0 ");
        text.Append(AudioStreams.VoiceKey).Append(" = true }");
        text.Append(" playback.props = { node.name = ").Append(Quote(name + ".output"));
        text.Append(" node.passive = true audio.position = [ FL FR ] ").Append(AudioStreams.VoiceKey).Append(" = true } }");
        return text.ToString();
    }

    private void AppendStereo(StringBuilder text)
    {
        text.Append(" { type = builtin name = copyL label = copy }");
        text.Append(" { type = builtin name = copyR label = copy }");
        if (ImpulseFile is not { } impulse)
        {
            text.Append(" ] inputs = [ \"copyL:In\" \"copyR:In\" ] outputs = [ \"copyL:Out\" \"copyR:Out\" ] }");
            return;
        }

        text.Append(" { type = builtin name = down label = mixer control = { \"Gain 1\" = 0.5 \"Gain 2\" = 0.5 } }");
        text.Append(" { type = builtin name = wetL label = convolver config = { filename = ").Append(Quote(impulse)).Append(" channel = 0 } }");
        text.Append(" { type = builtin name = wetR label = convolver config = { filename = ").Append(Quote(impulse)).Append(" channel = 1 } }");
        text.Append(" { type = builtin name = mixL label = mixer control = { \"Gain 1\" = 1.0 \"Gain 2\" = 0.0 } }");
        text.Append(" { type = builtin name = mixR label = mixer control = { \"Gain 1\" = 1.0 \"Gain 2\" = 0.0 } }");
        text.Append(" ] links = [");
        text.Append(" { output = \"copyL:Out\" input = \"down:In 1\" }");
        text.Append(" { output = \"copyR:Out\" input = \"down:In 2\" }");
        text.Append(" { output = \"down:Out\" input = \"wetL:In\" }");
        text.Append(" { output = \"down:Out\" input = \"wetR:In\" }");
        text.Append(" { output = \"copyL:Out\" input = \"mixL:In 1\" }");
        text.Append(" { output = \"copyR:Out\" input = \"mixR:In 1\" }");
        text.Append(" { output = \"wetL:Out\" input = \"mixL:In 2\" }");
        text.Append(" { output = \"wetR:Out\" input = \"mixR:In 2\" }");
        text.Append(" ] inputs = [ \"copyL:In\" \"copyR:In\" ] outputs = [ \"mixL:Out\" \"mixR:Out\" ] }");
    }

    private void AppendPlaced(StringBuilder text)
    {
        text.Append(" { type = builtin name = down label = mixer control = { \"Gain 1\" = 0.5 \"Gain 2\" = 0.5 } }");
        text.Append(" { type = builtin name = occlude label = bq_lowpass control = { \"Freq\" = 20000.0 \"Q\" = 0.707 } }");
        text.Append(" { type = builtin name = gain label = linear control = { \"Mult\" = 1.0 } }");
        if (Mode == VoiceMode.Hrtf)
        {
            text.Append(" { type = sofa name = hrtf label = spatializer config = { filename = ").Append(Quote(HrtfFile ?? string.Empty))
                .Append(" } control = { \"Azimuth\" = 0.0 \"Elevation\" = 0.0 \"Radius\" = ")
                .Append(HrtfRadiusMin.ToString("0.0##", CultureInfo.InvariantCulture)).Append(" } }");
        }
        else
        {
            text.Append(" { type = builtin name = left label = linear control = { \"Mult\" = 0.7071 } }");
            text.Append(" { type = builtin name = right label = linear control = { \"Mult\" = 0.7071 } }");
        }

        if (ImpulseFile is { } impulse)
        {
            text.Append(" { type = builtin name = wetL label = convolver config = { filename = ").Append(Quote(impulse)).Append(" channel = 0 } }");
            text.Append(" { type = builtin name = wetR label = convolver config = { filename = ").Append(Quote(impulse)).Append(" channel = 1 } }");
            text.Append(" { type = builtin name = mixL label = mixer control = { \"Gain 1\" = 1.0 \"Gain 2\" = 0.0 } }");
            text.Append(" { type = builtin name = mixR label = mixer control = { \"Gain 1\" = 1.0 \"Gain 2\" = 0.0 } }");
        }

        text.Append(" ] links = [");
        text.Append(" { output = \"down:Out\" input = \"occlude:In\" }");
        text.Append(" { output = \"occlude:Out\" input = \"gain:In\" }");
        var (dryLeft, dryRight) = Mode == VoiceMode.Hrtf ? ("hrtf:Out L", "hrtf:Out R") : ("left:Out", "right:Out");
        if (Mode == VoiceMode.Hrtf)
        {
            text.Append(" { output = \"gain:Out\" input = \"hrtf:In\" }");
        }
        else
        {
            text.Append(" { output = \"gain:Out\" input = \"left:In\" }");
            text.Append(" { output = \"gain:Out\" input = \"right:In\" }");
        }

        if (Reverb)
        {
            text.Append(" { output = \"gain:Out\" input = \"wetL:In\" }");
            text.Append(" { output = \"gain:Out\" input = \"wetR:In\" }");
            text.Append(" { output = ").Append(Quote(dryLeft)).Append(" input = \"mixL:In 1\" }");
            text.Append(" { output = ").Append(Quote(dryRight)).Append(" input = \"mixR:In 1\" }");
            text.Append(" { output = \"wetL:Out\" input = \"mixL:In 2\" }");
            text.Append(" { output = \"wetR:Out\" input = \"mixR:In 2\" }");
            dryLeft = "mixL:Out";
            dryRight = "mixR:Out";
        }

        text.Append(" ] inputs = [ \"down:In 1\" \"down:In 2\" ] outputs = [ ").Append(Quote(dryLeft)).Append(' ').Append(Quote(dryRight)).Append(" ] }");
    }

    private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
}
