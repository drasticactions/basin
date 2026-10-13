using Basin.Diagnostics;
using Tomlyn.Model;

namespace Basin.Config;

public sealed record WindowQuirks
{
    public static readonly string[] Keys =
        ["letterbox", "scale_filter", "readiness", "force_unmaximize", "allow_client_move", "emulated_modes"];

    public static WindowQuirks None { get; } = new();

    public LetterboxQuirk? Letterbox { get; init; }

    public TextureFilter? ScaleFilter { get; init; }

    public ReadinessQuirk? Readiness { get; init; }

    public bool? ForceUnmaximize { get; init; }

    public bool? AllowClientMove { get; init; }

    public bool? EmulatedModes { get; init; }

    public bool IsEmpty => this == None;

    public WindowQuirks Merge(WindowQuirks? weaker) => weaker is null ? this : new()
    {
        Letterbox = Letterbox ?? weaker.Letterbox,
        ScaleFilter = ScaleFilter ?? weaker.ScaleFilter,
        Readiness = Readiness ?? weaker.Readiness,
        ForceUnmaximize = ForceUnmaximize ?? weaker.ForceUnmaximize,
        AllowClientMove = AllowClientMove ?? weaker.AllowClientMove,
        EmulatedModes = EmulatedModes ?? weaker.EmulatedModes,
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
                _ => (LetterboxQuirk?)null,
            }, "fit, center or off"),
            ScaleFilter = Choice(take("scale_filter"), where("scale_filter"), log, static text => text switch
            {
                "linear" => TextureFilter.Linear,
                "nearest" => TextureFilter.Nearest,
                _ => (TextureFilter?)null,
            }, "linear or nearest"),
            Readiness = Choice(take("readiness"), where("readiness"), log, static text => text switch
            {
                "matching" => ReadinessQuirk.Matching,
                "immediate" => ReadinessQuirk.Immediate,
                _ => (ReadinessQuirk?)null,
            }, "matching or immediate"),
            ForceUnmaximize = Flag(take("force_unmaximize"), where("force_unmaximize"), log),
            AllowClientMove = Flag(take("allow_client_move"), where("allow_client_move"), log),
            EmulatedModes = Flag(take("emulated_modes"), where("emulated_modes"), log),
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
