using Basin.Diagnostics;
using Tomlyn.Model;

namespace Basin.Config;

/// <summary>
/// The quirks that a window rule asks for. A <see langword="null"/> field means that the rule has no opinion on it.
/// </summary>
public sealed record WindowQuirks
{
    /// <summary>
    /// The rule keys that <see cref="Read(TomlReader)"/> reads, so that a caller can skip them as unknown keys.
    /// </summary>
    public static readonly string[] Keys =
        ["letterbox", "scale_filter", "readiness", "force_unmaximize", "allow_client_move", "emulated_modes", "frame_limit",
         "inverse_tone_mapping", "nested_confine_lock"];

    /// <summary>
    /// Gets the set with no opinion on any quirk.
    /// </summary>
    public static WindowQuirks None { get; } = new();

    /// <summary>
    /// Gets how a window whose size differs from its configured size is shown.
    /// </summary>
    public LetterboxQuirk? Letterbox { get; init; }

    /// <summary>
    /// Gets the filter for a scaled window.
    /// </summary>
    public TextureFilter? ScaleFilter { get; init; }

    /// <summary>
    /// Gets when an X11 window is ready in a configure transaction.
    /// </summary>
    public ReadinessQuirk? Readiness { get; init; }

    /// <summary>
    /// Gets a value indicating whether a maximize request of an X11 window at map is ignored.
    /// </summary>
    public bool? ForceUnmaximize { get; init; }

    /// <summary>
    /// Gets a value indicating whether the position in a configure request of an X11 window is honored after map.
    /// </summary>
    public bool? AllowClientMove { get; init; }

    /// <summary>
    /// Gets a value indicating whether the RandR mode emulation of an X11 game is honored.
    /// </summary>
    public bool? EmulatedModes { get; init; }

    /// <summary>
    /// Gets the frame rate limit of the window, from 0 to 1000. Zero removes a limit.
    /// </summary>
    public int? FrameLimit { get; init; }

    /// <summary>
    /// Gets a value indicating whether the SDR content of the window can be inverse tone mapped on an HDR output.
    /// </summary>
    public bool? InverseToneMapping { get; init; }

    /// <summary>
    /// Gets a value indicating whether a nested compositor can lock the host pointer while this window confines it with a hidden cursor.
    /// </summary>
    public bool? NestedConfineLock { get; init; }

    /// <summary>
    /// Gets a value indicating whether the set has no opinion on any quirk.
    /// </summary>
    public bool IsEmpty => this == None;

    /// <summary>
    /// Combines two sets. Each field of this set wins, and a <see langword="null"/> field takes the value of <paramref name="weaker"/>.
    /// </summary>
    /// <param name="weaker">The set of a less specific rule, or <see langword="null"/>.</param>
    /// <returns>The combined set, or this instance when <paramref name="weaker"/> is <see langword="null"/>.</returns>
    public WindowQuirks Merge(WindowQuirks? weaker) => weaker is null ? this : new()
    {
        Letterbox = Letterbox ?? weaker.Letterbox,
        ScaleFilter = ScaleFilter ?? weaker.ScaleFilter,
        Readiness = Readiness ?? weaker.Readiness,
        ForceUnmaximize = ForceUnmaximize ?? weaker.ForceUnmaximize,
        AllowClientMove = AllowClientMove ?? weaker.AllowClientMove,
        EmulatedModes = EmulatedModes ?? weaker.EmulatedModes,
        FrameLimit = FrameLimit ?? weaker.FrameLimit,
        InverseToneMapping = InverseToneMapping ?? weaker.InverseToneMapping,
        NestedConfineLock = NestedConfineLock ?? weaker.NestedConfineLock,
    };

    /// <summary>
    /// Reads the quirk keys of a rule row. An invalid value is ignored with a warning, and a removed <c>x11_scale</c> key also gives a warning.
    /// </summary>
    /// <param name="row">The reader of the rule row.</param>
    /// <returns>The quirks of the row.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="row"/> is <see langword="null"/>.</exception>
    public static WindowQuirks Read(TomlReader row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return Parse(key => row.Raw(key), row.Where, row.Log);
    }

    /// <summary>
    /// Reads the quirk keys of a rule table. An invalid value is ignored with a warning, and a removed <c>x11_scale</c> key also gives a warning.
    /// </summary>
    /// <param name="table">The rule table.</param>
    /// <param name="log">The logger for warnings.</param>
    /// <returns>The quirks of the table.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="table"/> is <see langword="null"/>.</exception>
    public static WindowQuirks Read(TomlTable table, BasinLogger log)
    {
        ArgumentNullException.ThrowIfNull(table);
        return Parse(key => table.TryGetValue(key, out var value) ? value : null, static key => $"rule {key}", log);
    }

    private static WindowQuirks Parse(Func<string, object?> take, Func<string, string> where, BasinLogger log)
    {
        if (take("x11_scale") is not null)
        {
            log.Warn($"{where("x11_scale")} is removed, use a [[launch]] rule with x11 = \"low-density\" instead, ignored");
        }

        return new WindowQuirks
        {
            Letterbox = Choice(take("letterbox"), where("letterbox"), log, static text => text switch
            {
                "fit" => LetterboxQuirk.Fit,
                "center" => LetterboxQuirk.Center,
                "off" => LetterboxQuirk.Off,
                "integer" => LetterboxQuirk.Integer,
                "fill" => LetterboxQuirk.Fill,
                "stretch" => LetterboxQuirk.Stretch,
                _ => (LetterboxQuirk?)null,
            }, "fit, center, integer, fill, stretch or off"),
            ScaleFilter = Choice(take("scale_filter"), where("scale_filter"), log, static text => text switch
            {
                "linear" => TextureFilter.Linear,
                "nearest" => TextureFilter.Nearest,
                "pixel" => TextureFilter.Pixel,
                _ => (TextureFilter?)null,
            }, "linear, nearest or pixel"),
            Readiness = Choice(take("readiness"), where("readiness"), log, static text => text switch
            {
                "matching" => ReadinessQuirk.Matching,
                "immediate" => ReadinessQuirk.Immediate,
                _ => (ReadinessQuirk?)null,
            }, "matching or immediate"),
            ForceUnmaximize = Flag(take("force_unmaximize"), where("force_unmaximize"), log),
            AllowClientMove = Flag(take("allow_client_move"), where("allow_client_move"), log),
            EmulatedModes = Flag(take("emulated_modes"), where("emulated_modes"), log),
            FrameLimit = Count(take("frame_limit"), where("frame_limit"), log),
            InverseToneMapping = Flag(take("inverse_tone_mapping"), where("inverse_tone_mapping"), log),
            NestedConfineLock = Flag(take("nested_confine_lock"), where("nested_confine_lock"), log),
        };
    }

    private static T? Choice<T>(object? value, string where, BasinLogger log, Func<string, T?> parse, string allowed)
        where T : struct
    {
        if (value is null)
        {
            return null;
        }

        if (value is string text && parse(text) is { } parsed)
        {
            return parsed;
        }

        log.Warn($"{where} \"{value}\" is not {allowed}, ignored");
        return null;
    }

    private static int? Count(object? value, string where, BasinLogger log)
    {
        switch (value)
        {
            case null:
                return null;
            case long number when number is >= 0 and <= 1000:
                return (int)number;
            default:
                log.Warn($"{where} \"{value}\" is not a frame rate from 0 to 1000, ignored");
                return null;
        }
    }

    private static bool? Flag(object? value, string where, BasinLogger log)
    {
        switch (value)
        {
            case null:
                return null;
            case bool flag:
                return flag;
            default:
                log.Warn($"{where} \"{value}\" is not true or false, ignored");
                return null;
        }
    }
}
