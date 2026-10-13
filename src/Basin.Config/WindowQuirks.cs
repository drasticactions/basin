using Basin.Diagnostics;
using Tomlyn.Model;

namespace Basin.Config;

public sealed record WindowQuirks
{
    public static readonly string[] Keys =
        ["letterbox", "scale_filter", "readiness", "force_unmaximize", "allow_client_move", "emulated_modes", "frame_limit",
         "inverse_tone_mapping", "nested_confine_lock"];

    public static WindowQuirks None { get; } = new();

    public LetterboxQuirk? Letterbox { get; init; }

    public TextureFilter? ScaleFilter { get; init; }

    public ReadinessQuirk? Readiness { get; init; }

    public bool? ForceUnmaximize { get; init; }

    public bool? AllowClientMove { get; init; }

    public bool? EmulatedModes { get; init; }

    public int? FrameLimit { get; init; }

    public bool? InverseToneMapping { get; init; }

    public bool? NestedConfineLock { get; init; }

    public bool IsEmpty => this == None;

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

    public static WindowQuirks Read(TomlReader row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return Parse(key => row.Raw(key), row.Where, row.Log);
    }

    public static WindowQuirks Read(TomlTable table, BasinLogger log)
    {
        ArgumentNullException.ThrowIfNull(table);
        return Parse(key => table.TryGetValue(key, out var value) ? value : null, static key => $"rule {key}", log);
    }

    private static WindowQuirks Parse(Func<string, object?> take, Func<string, string> where, BasinLogger log)
    {
        if (take("x11_scale") is not null)
        {
            log.Warn($"{where("x11_scale")} is removed, ignored: a [[launch]] rule with x11 = \"low-density\" starts an application on the low-density X server");
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
