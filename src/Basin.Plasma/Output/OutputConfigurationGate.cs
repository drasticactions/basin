using Basin.Capabilities;

namespace Basin.Plasma;

internal static class OutputConfigurationGate
{
    public static string? Reject(ref OutputConfigurationEntry entry, OutputConfigurationFeatures features) =>
        Walk(ref entry, features, true);

    public static void Clear(ref OutputConfigurationEntry entry, OutputConfigurationFeatures features) =>
        Walk(ref entry, features, false);

    private static string? Walk(ref OutputConfigurationEntry entry, OutputConfigurationFeatures features, bool strict)
    {
        if (Lacks(features, OutputConfigurationFeatures.Overscan) && entry.Overscan is { } overscan)
        {
            if (strict && overscan != 0)
            {
                return "overscan";
            }

            entry = entry with { Overscan = null };
        }

        if (Lacks(features, OutputConfigurationFeatures.Vrr) && entry.VrrPolicy is { } vrr)
        {
            if (strict && vrr != OutputVrrPolicy.Automatic)
            {
                return "the vrr policy";
            }

            entry = entry with { VrrPolicy = null };
        }

        if (Lacks(features, OutputConfigurationFeatures.RgbRange) && entry.RgbRange is { } rgb)
        {
            if (strict && rgb != OutputRgbRange.Automatic)
            {
                return "the rgb range";
            }

            entry = entry with { RgbRange = null };
        }

        if (Lacks(features, OutputConfigurationFeatures.HighDynamicRange))
        {
            if (strict && entry.HighDynamicRange == true)
            {
                return "high dynamic range";
            }

            if (strict && entry.BrightnessOverrides is { } overrides && overrides != OutputBrightnessOverrides.None)
            {
                return "brightness overrides";
            }

            entry = entry with { HighDynamicRange = null, SdrBrightnessNits = null, BrightnessOverrides = null };
        }

        if (Lacks(features, OutputConfigurationFeatures.WideColorGamut))
        {
            if (strict && entry.WideColorGamut == true)
            {
                return "a wide color gamut";
            }

            if (strict && entry.SdrGamutWideness is { } wideness && wideness != 0)
            {
                return "sdr gamut wideness";
            }

            entry = entry with { WideColorGamut = null, SdrGamutWideness = null };
        }

        if (Lacks(features, OutputConfigurationFeatures.AutoRotate) && entry.AutoRotate is { } rotate)
        {
            if (strict && rotate != OutputAutoRotatePolicy.Never)
            {
                return "auto rotate";
            }

            entry = entry with { AutoRotate = null };
        }

        if (Lacks(features, OutputConfigurationFeatures.IccProfile) && entry.IccProfilePath is { } icc)
        {
            if (strict && icc.Length > 0)
            {
                return "icc profiles";
            }

            entry = entry with { IccProfilePath = null };
        }

        if (Lacks(features, OutputConfigurationFeatures.HdrIccProfile) && entry.HdrIccProfilePath is { } hdrIcc)
        {
            if (strict && hdrIcc.Length > 0)
            {
                return "hdr icc profiles";
            }

            entry = entry with { HdrIccProfilePath = null };
        }

        if (ProfileSourceGate(entry.ColorProfileSource, features) is { } source)
        {
            if (strict && source.Length > 0)
            {
                return source;
            }

            entry = entry with { ColorProfileSource = null };
        }

        if (ProfileSourceGate(entry.HdrColorProfileSource, features) is { } hdrSource)
        {
            if (strict && hdrSource.Length > 0)
            {
                return hdrSource;
            }

            entry = entry with { HdrColorProfileSource = null };
        }

        if (Lacks(features, OutputConfigurationFeatures.Brightness))
        {
            if (strict && entry.Brightness is { } brightness && brightness != 10000)
            {
                return "the brightness setting";
            }

            if (strict && entry.Dimming is { } dimming && dimming != 10000)
            {
                return "dimming";
            }

            entry = entry with { Brightness = null, Dimming = null };
        }

        if (Lacks(features, OutputConfigurationFeatures.MaxBitsPerColor) && entry.MaxBitsPerColor is { } maxBpc)
        {
            if (strict && maxBpc is not (0 or 8))
            {
                return "max bits per color";
            }

            entry = entry with { MaxBitsPerColor = null };
        }

        if (Lacks(features, OutputConfigurationFeatures.Edr) && entry.EdrPolicy is { } edr)
        {
            if (strict && edr != OutputEdrPolicy.Never)
            {
                return "edr";
            }

            entry = entry with { EdrPolicy = null };
        }

        if (Lacks(features, OutputConfigurationFeatures.Sharpness) && entry.Sharpness is { } sharpness)
        {
            if (strict && sharpness != 0)
            {
                return "sharpness";
            }

            entry = entry with { Sharpness = null };
        }

        if (Lacks(features, OutputConfigurationFeatures.CustomModes) && entry.CustomModes is { } modes)
        {
            if (strict && modes.Count > 0)
            {
                return "custom modes";
            }

            entry = entry with { CustomModes = null };
        }

        if (Lacks(features, OutputConfigurationFeatures.AutoBrightness) && entry.AutoBrightness is { } auto)
        {
            if (strict && auto)
            {
                return "automatic brightness";
            }

            entry = entry with { AutoBrightness = null };
        }

        if (Lacks(features, OutputConfigurationFeatures.AbmLevel) && entry.AbmLevel is { } abm)
        {
            if (strict && abm != 0)
            {
                return "the abm level";
            }

            entry = entry with { AbmLevel = null };
        }

        return null;
    }

    private static bool Lacks(OutputConfigurationFeatures features, OutputConfigurationFeatures feature) =>
        (features & feature) == 0;

    private static string? ProfileSourceGate(
        OutputColorProfileSource? source, OutputConfigurationFeatures features) => source switch
    {
        OutputColorProfileSource.Icc when Lacks(features, OutputConfigurationFeatures.IccProfile) =>
            "an icc color profile source",
        OutputColorProfileSource.Edid when Lacks(features, OutputConfigurationFeatures.BuiltInColor) =>
            "the built-in color profile",
        OutputColorProfileSource.Srgb when Lacks(features, OutputConfigurationFeatures.IccProfile) &&
            Lacks(features, OutputConfigurationFeatures.BuiltInColor) => string.Empty,
        _ => null,
    };
}
