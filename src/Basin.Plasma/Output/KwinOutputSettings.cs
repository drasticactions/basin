using System.Security.Cryptography;
using System.Text.Json;
using Basin.Capabilities;

namespace Basin.Plasma;

public sealed class KwinOutputSettings
{
    public const string FileName = "kwinoutputconfig.json";

    private readonly List<Stored> _outputs;
    private readonly List<Setup> _setups;

    private sealed record Stored
    {
        public string Identifier { get; init; } = string.Empty;

        public string Hash { get; init; } = string.Empty;

        public string ConnectorName { get; init; } = string.Empty;

        public string Uuid { get; init; } = string.Empty;

        public bool Usable { get; init; }

        public OutputMode? Mode { get; init; }

        public double? Scale { get; init; }

        public OutputTransform? Transform { get; init; }

        public uint? Overscan { get; init; }

        public OutputRgbRange? RgbRange { get; init; }

        public OutputVrrPolicy? VrrPolicy { get; init; }

        public bool? HighDynamicRange { get; init; }

        public uint? SdrBrightnessNits { get; init; }

        public bool? WideColorGamut { get; init; }

        public OutputAutoRotatePolicy? AutoRotate { get; init; }

        public string? IccProfilePath { get; init; }

        public string? HdrIccProfilePath { get; init; }

        public OutputBrightnessOverrides? BrightnessOverrides { get; init; }

        public uint? SdrGamutWideness { get; init; }

        public OutputColorProfileSource? ColorProfileSource { get; init; }

        public OutputColorProfileSource? HdrColorProfileSource { get; init; }

        public uint? Brightness { get; init; }

        public OutputColorPowerTradeoff? ColorPowerTradeoff { get; init; }

        public bool? DdcCiAllowed { get; init; }

        public uint? MaxBitsPerColor { get; init; }

        public OutputEdrPolicy? EdrPolicy { get; init; }

        public uint? Sharpness { get; init; }

        public IReadOnlyList<OutputMode>? CustomModes { get; init; }

        public bool? AutoBrightness { get; init; }

        public uint? AbmLevel { get; init; }
    }

    private sealed record Placement(int Index, bool Enabled, Point Position, uint? Priority, string? ReplicationSource);

    private sealed record Setup(bool LidClosed, List<Placement> Outputs);

    private readonly record struct Identity(string Identifier, string Hash, string ConnectorName);

    private KwinOutputSettings(List<Stored> outputs, List<Setup> setups)
    {
        _outputs = outputs;
        _setups = setups;
    }

    public int Count => _outputs.Count;

    public static string? Locate()
    {
        var home = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrEmpty(home))
        {
            home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }

        var candidate = Path.Combine(home, FileName);
        if (File.Exists(candidate))
        {
            return candidate;
        }

        var dirs = Environment.GetEnvironmentVariable("XDG_CONFIG_DIRS");
        if (string.IsNullOrEmpty(dirs))
        {
            dirs = "/etc/xdg";
        }

        foreach (var dir in dirs.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            candidate = Path.Combine(dir, FileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static bool TryLoad(out KwinOutputSettings settings)
    {
        if (Locate() is { } path)
        {
            return TryLoad(path, out settings);
        }

        settings = new KwinOutputSettings([], []);
        return false;
    }

    public static bool TryLoad(string path, out KwinOutputSettings settings)
    {
        ArgumentNullException.ThrowIfNull(path);
        settings = new KwinOutputSettings([], []);
        try
        {
            return File.Exists(path) && TryParse(File.ReadAllText(path), out settings);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static bool TryParse(string json, out KwinOutputSettings settings)
    {
        ArgumentNullException.ThrowIfNull(json);
        settings = new KwinOutputSettings([], []);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return false;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            if (!TryGroup(document.RootElement, "outputs", out var outputs) ||
                !TryGroup(document.RootElement, "setups", out var setups))
            {
                return false;
            }

            var stored = new List<Stored>();
            foreach (var element in outputs.EnumerateArray())
            {
                var read = ReadStored(element);
                if (read.Usable && stored.Exists(other => other.Usable &&
                    other.Identifier == read.Identifier && other.Hash == read.Hash &&
                    other.ConnectorName == read.ConnectorName))
                {
                    read = read with { Usable = false };
                }

                stored.Add(read);
            }

            var parsed = new List<Setup>();
            foreach (var element in setups.EnumerateArray())
            {
                if (ReadSetup(element, stored) is { } setup)
                {
                    parsed.Add(setup);
                }
            }

            settings = new KwinOutputSettings(stored, parsed);
            return stored.Count > 0;
        }
    }

    public IReadOnlyList<OutputConfigurationEntry> EntriesFor(IReadOnlyList<IOutput> outputs)
    {
        ArgumentNullException.ThrowIfNull(outputs);
        var identities = new Identity[outputs.Count];
        for (var i = 0; i < outputs.Count; i++)
        {
            identities[i] = IdentityOf(outputs[i]);
        }

        var indices = new int[outputs.Count];
        for (var i = 0; i < outputs.Count; i++)
        {
            indices[i] = Match(identities, i);
        }

        var setup = FindSetup(indices);
        var entries = new List<OutputConfigurationEntry>(outputs.Count);
        for (var i = 0; i < outputs.Count; i++)
        {
            if (indices[i] < 0)
            {
                continue;
            }

            var entry = Fill(outputs[i], _outputs[indices[i]]);
            if (setup is not null && setup.Outputs.Find(placement => placement.Index == indices[i]) is { } placed)
            {
                entry = entry with
                {
                    Enabled = placed.Enabled,
                    Position = placed.Position,
                    ReplicationSourceUuid = placed.ReplicationSource,
                };
                if (placed.Priority is { } priority)
                {
                    entry = entry with { Priority = priority };
                }
            }

            entries.Add(entry);
        }

        return entries;
    }

    public bool Apply(IOutputConfiguration configuration, IReadOnlyList<IOutput> outputs) =>
        Apply(configuration, EntriesFor(outputs));

    public bool Apply(IOutputConfiguration configuration, IReadOnlyList<OutputConfigurationEntry> requested)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(requested);
        var entries = new List<OutputConfigurationEntry>(requested);
        if (entries.Count == 0)
        {
            return false;
        }

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            OutputConfigurationGate.Clear(ref entry, configuration.Supported(entry.Output));
            entries[i] = entry;
        }

        if (configuration.Test(entries))
        {
            return configuration.Apply(entries);
        }

        for (var i = 0; i < entries.Count; i++)
        {
            entries[i] = entries[i] with { Mode = null, CustomModes = null };
        }

        return configuration.Test(entries) && configuration.Apply(entries);
    }

    private static bool TryGroup(JsonElement root, string name, out JsonElement data)
    {
        foreach (var element in root.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty("name", out var group) && group.ValueKind == JsonValueKind.String &&
                group.ValueEquals(name) &&
                element.TryGetProperty("data", out data) && data.ValueKind == JsonValueKind.Array)
            {
                return true;
            }
        }

        data = default;
        return false;
    }

    private static Stored ReadStored(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return new Stored();
        }

        var identifier = Text(element, "edidIdentifier");
        var hash = Text(element, "edidHash");
        var connector = Text(element, "connectorName");
        return new Stored
        {
            Identifier = identifier,
            Hash = hash,
            ConnectorName = connector,
            Uuid = Text(element, "uuid"),
            Usable = identifier.Length > 0 || hash.Length > 0 || connector.Length > 0,
            Mode = element.TryGetProperty("mode", out var mode) ? ReadMode(mode) : null,
            Scale = Number(element, "scale") is { } scale && scale > 0 && scale <= 5 ? scale : null,
            Transform = ReadTransform(Text(element, "transform")),
            Overscan = Number(element, "overscan") is { } overscan && overscan >= 0 && overscan <= 100
                ? (uint)overscan
                : null,
            RgbRange = Text(element, "rgbRange") switch
            {
                "Automatic" => OutputRgbRange.Automatic,
                "Limited" => OutputRgbRange.Limited,
                "Full" => OutputRgbRange.Full,
                _ => null,
            },
            VrrPolicy = Text(element, "vrrPolicy") switch
            {
                "Never" => OutputVrrPolicy.Never,
                "Automatic" => OutputVrrPolicy.Automatic,
                "Always" => OutputVrrPolicy.Always,
                _ => null,
            },
            HighDynamicRange = Flag(element, "highDynamicRange"),
            SdrBrightnessNits = Number(element, "sdrBrightness") is { } sdr && sdr > 0 ? (uint)sdr : null,
            WideColorGamut = Flag(element, "wideColorGamut"),
            AutoRotate = Text(element, "autoRotation") switch
            {
                "Never" => OutputAutoRotatePolicy.Never,
                "InTabletMode" => OutputAutoRotatePolicy.InTabletMode,
                "Always" => OutputAutoRotatePolicy.Always,
                _ => null,
            },
            IccProfilePath = element.TryGetProperty("iccProfilePath", out _) ? Text(element, "iccProfilePath") : null,
            HdrIccProfilePath = element.TryGetProperty("hdrIccProfilePath", out _)
                ? Text(element, "hdrIccProfilePath")
                : null,
            BrightnessOverrides = ReadBrightnessOverrides(element),
            SdrGamutWideness = Number(element, "sdrGamutWideness") is { } wideness
                ? Fraction(wideness)
                : null,
            ColorProfileSource = ReadProfileSource(Text(element, "colorProfileSource")),
            HdrColorProfileSource = ReadProfileSource(Text(element, "hdrColorProfileSource")),
            Brightness = Number(element, "brightness") is { } brightness ? Fraction(brightness) : null,
            ColorPowerTradeoff = Text(element, "colorPowerTradeoff") switch
            {
                "PreferEfficiency" => OutputColorPowerTradeoff.Efficiency,
                "PreferAccuracy" => OutputColorPowerTradeoff.Accuracy,
                _ => null,
            },
            DdcCiAllowed = Flag(element, "allowDdcCi"),
            MaxBitsPerColor = Number(element, "maxBitsPerColor") is { } bpc && bpc >= 6 && bpc <= 16
                ? (uint)bpc
                : null,
            EdrPolicy = Text(element, "edrPolicy") switch
            {
                "never" => OutputEdrPolicy.Never,
                "always" => OutputEdrPolicy.Always,
                _ => null,
            },
            Sharpness = Number(element, "sharpness") is { } sharpness ? Fraction(sharpness) : null,
            CustomModes = ReadCustomModes(element),
            AutoBrightness = Flag(element, "automaticBrightness"),
            AbmLevel = Number(element, "abmLevel") is { } abm && abm >= 0 && abm <= 4 ? (uint)abm : null,
        };
    }

    private static Setup? ReadSetup(JsonElement element, List<Stored> stored)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty("outputs", out var outputs) || outputs.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var placements = new List<Placement>();
        foreach (var output in outputs.EnumerateArray())
        {
            if (output.ValueKind != JsonValueKind.Object ||
                Flag(output, "enabled") is not { } enabled ||
                Number(output, "outputIndex") is not { } index)
            {
                return null;
            }

            var slot = (int)index;
            if (slot < 0 || slot >= stored.Count || !stored[slot].Usable ||
                placements.Exists(placement => placement.Index == slot))
            {
                return null;
            }

            if (!output.TryGetProperty("position", out var position) ||
                Number(position, "x") is not { } x || Number(position, "y") is not { } y)
            {
                return null;
            }

            uint? priority = null;
            if (Number(output, "priority") is { } value)
            {
                if (value < 0)
                {
                    if (enabled)
                    {
                        return null;
                    }
                }
                else
                {
                    priority = (uint)value;
                }
            }

            var replication = Text(output, "replicationSource");
            placements.Add(new Placement(
                slot,
                enabled,
                new Point((int)x, (int)y),
                priority,
                replication.Length > 0 && replication != stored[slot].Uuid ? replication : null));
        }

        if (placements.Count == 0 || !placements.Exists(placement => placement.Enabled))
        {
            return null;
        }

        return new Setup(Flag(element, "lidClosed") ?? false, placements);
    }

    private static OutputMode? ReadMode(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (element.TryGetProperty("basic", out var basic) && basic.ValueKind == JsonValueKind.Object)
        {
            return ReadBasicMode(basic);
        }

        if (element.TryGetProperty("cvt", out var cvt) && cvt.ValueKind == JsonValueKind.Object)
        {
            return ReadCvtMode(cvt);
        }

        return ReadBasicMode(element);
    }

    private static OutputMode? ReadBasicMode(JsonElement element)
    {
        if (Number(element, "width") is not { } width || width <= 0 ||
            Number(element, "height") is not { } height || height <= 0 ||
            Number(element, "refreshRate") is not { } refresh || refresh <= 0)
        {
            return null;
        }

        return new OutputMode((int)width, (int)height, (int)refresh);
    }

    private static OutputMode? ReadCvtMode(JsonElement element)
    {
        if (Number(element, "clock") is not { } clock || clock <= 0 ||
            Number(element, "hdisplay") is not { } width || width <= 0 ||
            Number(element, "vdisplay") is not { } height || height <= 0 ||
            Number(element, "htotal") is not { } htotal || htotal <= 0 ||
            Number(element, "vtotal") is not { } vtotal || vtotal <= 0)
        {
            return null;
        }

        var refresh = clock * 1_000_000.0 / (htotal * vtotal);
        return new OutputMode((int)width, (int)height, (int)Math.Round(refresh));
    }

    private static IReadOnlyList<OutputMode>? ReadCustomModes(JsonElement element)
    {
        if (!element.TryGetProperty("customModes", out var modes) || modes.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var parsed = new List<OutputMode>();
        foreach (var mode in modes.EnumerateArray())
        {
            if (ReadMode(mode) is { } read)
            {
                parsed.Add(read);
            }
        }

        return parsed;
    }

    private static OutputBrightnessOverrides? ReadBrightnessOverrides(JsonElement element)
    {
        var peak = Number(element, "maxPeakBrightnessOverride");
        var average = Number(element, "maxAverageBrightnessOverride");
        var minimum = Number(element, "minBrightnessOverride");
        if (peak is null && average is null && minimum is null)
        {
            return null;
        }

        return new OutputBrightnessOverrides(
            peak is { } peakValue && peakValue >= 50 ? (int)Math.Round(peakValue) : -1,
            average is { } averageValue && averageValue >= 50 ? (int)Math.Round(averageValue) : -1,
            minimum is { } minimumValue && minimumValue >= 0 ? (int)Math.Round(minimumValue * 10_000) : -1);
    }

    private static OutputColorProfileSource? ReadProfileSource(string source) => source switch
    {
        "sRGB" => OutputColorProfileSource.Srgb,
        "ICC" => OutputColorProfileSource.Icc,
        "EDID" => OutputColorProfileSource.Edid,
        _ => null,
    };

    private static OutputTransform? ReadTransform(string transform) => transform switch
    {
        "Normal" => OutputTransform.Normal,
        "Rotated90" => OutputTransform.Rotate90,
        "Rotated180" => OutputTransform.Rotate180,
        "Rotated270" => OutputTransform.Rotate270,
        "Flipped" => OutputTransform.Flipped,
        "Flipped90" => OutputTransform.Flipped90,
        "Flipped180" => OutputTransform.Flipped180,
        "Flipped270" => OutputTransform.Flipped270,
        _ => null,
    };

    private static uint Fraction(double value) => (uint)Math.Round(Math.Clamp(value, 0, 1) * 10_000);

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static double? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetDouble(out var number)
            ? number
            : null;

    private static bool? Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private OutputConfigurationEntry Fill(IOutput output, Stored stored)
    {
        return new OutputConfigurationEntry
        {
            Output = output,
            Enabled = output.Enabled,
            Mode = ResolveMode(output, stored.Mode),
            Scale = stored.Scale,
            Transform = stored.Transform,
            Overscan = stored.Overscan,
            RgbRange = stored.RgbRange,
            VrrPolicy = stored.VrrPolicy,
            HighDynamicRange = stored.HighDynamicRange,
            SdrBrightnessNits = stored.SdrBrightnessNits,
            WideColorGamut = stored.WideColorGamut,
            AutoRotate = stored.AutoRotate,
            IccProfilePath = stored.IccProfilePath,
            HdrIccProfilePath = stored.HdrIccProfilePath,
            BrightnessOverrides = stored.BrightnessOverrides,
            SdrGamutWideness = stored.SdrGamutWideness,
            ColorProfileSource = stored.ColorProfileSource,
            HdrColorProfileSource = stored.HdrColorProfileSource,
            Brightness = stored.Brightness,
            ColorPowerTradeoff = stored.ColorPowerTradeoff,
            DdcCiAllowed = stored.DdcCiAllowed,
            MaxBitsPerColor = stored.MaxBitsPerColor,
            EdrPolicy = stored.EdrPolicy,
            Sharpness = stored.Sharpness,
            CustomModes = stored.CustomModes,
            AutoBrightness = stored.AutoBrightness,
            AbmLevel = stored.AbmLevel,
        };
    }

    private static OutputMode? ResolveMode(IOutput output, OutputMode? mode)
    {
        if (mode is not { } wanted)
        {
            return null;
        }

        if ((output as Backend.Drm.DrmOutput)?.Modes is not { } available)
        {
            return wanted;
        }

        OutputMode? nearest = null;
        foreach (var candidate in available)
        {
            if (candidate == wanted)
            {
                return candidate;
            }

            if (candidate.Width != wanted.Width || candidate.Height != wanted.Height)
            {
                continue;
            }

            if (nearest is not { } best ||
                Math.Abs(candidate.RefreshMilliHz - wanted.RefreshMilliHz) <
                Math.Abs(best.RefreshMilliHz - wanted.RefreshMilliHz))
            {
                nearest = candidate;
            }
        }

        return nearest;
    }

    private Setup? FindSetup(int[] indices)
    {
        Setup? best = null;
        var bestCount = 0;
        foreach (var setup in _setups)
        {
            if (setup.LidClosed)
            {
                continue;
            }

            var matched = 0;
            foreach (var index in indices)
            {
                if (index >= 0 && setup.Outputs.Exists(placement => placement.Index == index))
                {
                    matched++;
                }
            }

            if (matched == setup.Outputs.Count && matched == Matched(indices))
            {
                return setup;
            }

            if (matched > bestCount)
            {
                best = setup;
                bestCount = matched;
            }
        }

        return best;
    }

    private static int Matched(int[] indices)
    {
        var count = 0;
        foreach (var index in indices)
        {
            if (index >= 0)
            {
                count++;
            }
        }

        return count;
    }

    private int Match(Identity[] identities, int slot)
    {
        var identity = identities[slot];
        if (identity.Identifier.Length == 0 && identity.Hash.Length == 0)
        {
            return Filter(string.Empty, string.Empty, identity.ConnectorName, out var named) == 1 ? named : -1;
        }

        var identifierUnique = identity.Identifier.Length > 0 && Unique(identities, identity, true);
        var hashUnique = Unique(identities, identity, false);

        var identifier = identity.Identifier;
        var matches = Filter(identifier, null, null, out var first);
        if (matches == 1 && identifierUnique)
        {
            return first;
        }

        if (matches == 0)
        {
            identifier = string.Empty;
        }

        if (hashUnique)
        {
            matches = Filter(identifier, identity.Hash, null, out first);
            if (matches == 1)
            {
                return first;
            }

            if (matches == 0)
            {
                return -1;
            }
        }

        return Filter(identifier, identity.Hash, identity.ConnectorName, out first) > 0 ? first : -1;
    }

    private int Filter(string? identifier, string? hash, string? connector, out int first)
    {
        var count = 0;
        first = -1;
        for (var i = 0; i < _outputs.Count; i++)
        {
            var stored = _outputs[i];
            if (!stored.Usable ||
                (identifier is not null && stored.Identifier != identifier) ||
                (hash is not null && stored.Hash != hash) ||
                (connector is not null && stored.ConnectorName != connector))
            {
                continue;
            }

            if (count == 0)
            {
                first = i;
            }

            count++;
        }

        return count;
    }

    private static bool Unique(Identity[] identities, in Identity identity, bool byIdentifier)
    {
        var count = 0;
        foreach (var other in identities)
        {
            if (byIdentifier ? other.Identifier == identity.Identifier : other.Hash == identity.Hash)
            {
                count++;
            }
        }

        return count == 1;
    }

    private static Identity IdentityOf(IOutput output)
    {
        var edid = output.EdidBytes.Span;
        if (edid.Length == 0)
        {
            return new Identity(string.Empty, string.Empty, output.Name);
        }

        Span<byte> digest = stackalloc byte[16];
        MD5.HashData(edid, digest);
        return new Identity(EdidIdentifier(edid), Convert.ToHexStringLower(digest), output.Name);
    }

    private static string EdidIdentifier(ReadOnlySpan<byte> edid)
    {
        if (edid.Length < 18)
        {
            return string.Empty;
        }

        var vendor = (edid[8] << 8) | edid[9];
        Span<char> manufacturer =
        [
            (char)(((vendor >> 10) & 0x1f) + 'A' - 1),
            (char)(((vendor >> 5) & 0x1f) + 'A' - 1),
            (char)((vendor & 0x1f) + 'A' - 1),
        ];
        foreach (var letter in manufacturer)
        {
            if (letter is < 'A' or > 'Z')
            {
                return string.Empty;
            }
        }

        var product = edid[10] | (edid[11] << 8);
        var serial = (uint)(edid[12] | (edid[13] << 8) | (edid[14] << 16) | (edid[15] << 24));
        var week = edid[16];
        var year = edid[17];
        var modelYear = week == 0xff ? year + 1990 : 0;
        var manufactureWeek = week == 0xff ? 0 : week;
        var manufactureYear = week == 0xff ? 0 : year + 1990;
        return $"{new string(manufacturer)} {product} {serial} {manufactureWeek} {manufactureYear} {modelYear}";
    }
}
