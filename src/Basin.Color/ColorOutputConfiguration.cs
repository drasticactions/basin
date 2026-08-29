using Basin.Capabilities;

namespace Basin.Color;

public sealed class ColorOutputConfiguration : IOutputConfiguration
{
    private const OutputConfigurationFeatures ColorMask =
        OutputConfigurationFeatures.HighDynamicRange |
        OutputConfigurationFeatures.WideColorGamut |
        OutputConfigurationFeatures.IccProfile |
        OutputConfigurationFeatures.HdrIccProfile |
        OutputConfigurationFeatures.BuiltInColor;

    private readonly IOutputConfiguration _inner;
    private readonly Dictionary<IOutput, OutputColorState> _states = [];
    private readonly Dictionary<IOutput, Action> _cleanup = [];
    private readonly Dictionary<IOutput, double[]> _routedCtm = [];
    private readonly Dictionary<IOutput, OutputGammaRamps> _routedGamma = [];
    private string? _failure;

    public ColorOutputConfiguration(IOutputConfiguration inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    public event Action<IReadOnlyList<OutputConfigurationEntry>>? Applied;

    public IOutputBrightness? Brightness { get; set; }

    public event Action<IOutput>? EdrChanged;

    private const double ZeroBrightnessLuminance = 0.04;
    private const double MaxEdrHeadroom = 3.0;
    private readonly Dictionary<IOutput, double> _edrDemand = [];
    private readonly Dictionary<IOutput, double> _edrHeadroom = [];

    public double EdrHeadroomOf(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return _edrHeadroom.GetValueOrDefault(output, 1.0);
    }

    public void SetEdrDemand(IOutput output, double demand)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (Math.Abs(_edrDemand.GetValueOrDefault(output, 1.0) - demand) < 0.001)
        {
            return;
        }

        _edrDemand[output] = demand;
        ReevaluateEdr(output);
    }

    private bool EdrCapable(IOutput output) =>
        Brightness is { } control && control.Supports(output) && control.Max(output) > 0 &&
        InternalConnectors.IsInternal(output) &&
        (output.Features & OutputConfigurationFeatures.HighDynamicRange) == 0;

    private void ReevaluateEdr(IOutput output)
    {
        var state = StateOf(output);
        var headroom = 1.0;
        if (EdrCapable(output) && state.EdrPolicy == OutputEdrPolicy.Always && !state.HighDynamicRange)
        {
            var brightness = state.Brightness / 10000.0;
            var maxPossible = Math.Min(
                (1 + ZeroBrightnessLuminance) / (ZeroBrightnessLuminance + brightness), MaxEdrHeadroom);
            headroom = Math.Clamp(_edrDemand.GetValueOrDefault(output, 1.0), 1.0, maxPossible);
        }

        if (Math.Abs(headroom - EdrHeadroomOf(output)) < 0.001)
        {
            return;
        }

        _edrHeadroom[output] = headroom;
        ApplyEdrBacklight(output, StateOf(output), headroom);
        EdrChanged?.Invoke(output);
    }

    private void ApplyEdrBacklight(IOutput output, OutputColorState state, double headroom)
    {
        if (Brightness is not { } control || !control.Supports(output) || control.Max(output) == 0 ||
            (!state.DdcCiAllowed && control.UsesDdcCi(output)))
        {
            return;
        }

        var brightness = state.Brightness / 10000.0;
        var effective = Math.Clamp(
            (ZeroBrightnessLuminance + brightness) * headroom - ZeroBrightnessLuminance, 0, 1);
        _ = control.Set(output, (uint)Math.Round(effective * control.Max(output)));
    }

    public string? LastFailureReason => _failure ?? _inner.LastFailureReason;

    public OutputConfigurationFeatures Supported(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var features = _inner.Supported(output) | (output.Features & ColorMask) | OutputConfigurationFeatures.Brightness;
        if (Brightness is { } control && control.UsesDdcCi(output))
        {
            features |= OutputConfigurationFeatures.DdcCi;
        }

        if (EdrCapable(output))
        {
            features |= OutputConfigurationFeatures.Edr;
        }

        return features;
    }

    public bool Test(IReadOnlyList<OutputConfigurationEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _failure = null;
        foreach (var entry in entries)
        {
            if (!ValidateProfiles(entry))
            {
                return false;
            }
        }

        return _inner.Test(entries);
    }

    public bool Apply(IReadOnlyList<OutputConfigurationEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _failure = null;
        foreach (var entry in entries)
        {
            if (!ValidateProfiles(entry))
            {
                return false;
            }
        }

        IReadOnlyList<OutputConfigurationEntry> adjusted = entries;
        List<OutputConfigurationEntry>? copy = null;
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            var tradeoff = entry.ColorPowerTradeoff ?? StateOf(entry.Output).ColorPowerTradeoff;
            if (tradeoff == OutputColorPowerTradeoff.Accuracy && entry.AbmLevel is > 0)
            {
                copy ??= new List<OutputConfigurationEntry>(entries);
                copy[i] = entry with { AbmLevel = 0 };
                adjusted = copy;
            }
        }

        if (!_inner.Apply(adjusted))
        {
            return false;
        }

        foreach (var entry in adjusted)
        {
            ApplyColor(entry);
        }

        Applied?.Invoke(adjusted);
        return true;
    }

    public bool TryRead(IOutput output, out OutputConfigurationEntry state)
    {
        ArgumentNullException.ThrowIfNull(output);
        var any = _inner.TryRead(output, out state);
        var color = StateOf(output);
        {
            if (!any)
            {
                state = new OutputConfigurationEntry { Output = output, Enabled = output.Enabled };
            }

            state = state with
            {
                HighDynamicRange = color.HighDynamicRange,
                WideColorGamut = color.WideColorGamut,
                SdrBrightnessNits = color.SdrBrightnessNits,
                SdrGamutWideness = color.SdrGamutWideness,
                ColorProfileSource = color.Source,
                IccProfilePath = color.IccProfilePath ?? string.Empty,
                HdrColorProfileSource = color.HdrSource,
                HdrIccProfilePath = color.HdrIccProfilePath ?? string.Empty,
                BrightnessOverrides = color.BrightnessOverrides,
                EdrPolicy = color.EdrPolicy,
                DdcCiAllowed = color.DdcCiAllowed,
                ColorPowerTradeoff = color.ColorPowerTradeoff,
                Brightness = color.Brightness,
                Dimming = color.Dimming,
            };
            return true;
        }
    }

    public void Seed(IOutput output, OutputColorState state)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(state);
        Record(output, state);
    }

    public OutputColorState StateOf(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return _states.GetValueOrDefault(output) ?? new OutputColorState();
    }

    public ImageDescription DescriptionOf(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var state = StateOf(output);
        var colorimetry = output.Colorimetry;
        if (state.HighDynamicRange)
        {
            var description = OutputDescriptions.Hdr10(
                state.BrightnessOverrides.MaxPeakBrightness >= 0
                    ? state.BrightnessOverrides.MaxPeakBrightness
                    : colorimetry?.MaxLuminance ?? 0,
                state.BrightnessOverrides.MinBrightness >= 0
                    ? state.BrightnessOverrides.MinBrightness / 10000.0
                    : colorimetry?.MinLuminance ?? 0);
            if (description.Luminances is { } luminances)
            {
                description = description with
                {
                    Luminances = (luminances.Min, luminances.Max, state.SdrBrightnessNits),
                };
            }

            return description;
        }

        if (state.Source == OutputColorProfileSource.Icc && state.IccProfilePath is { } path &&
            TryReadProfile(path, out var icc))
        {
            return new ImageDescription { IccData = icc };
        }

        ImageDescription sdr;
        if (state.Source == OutputColorProfileSource.Edid || state.SdrGamutWideness >= 10000)
        {
            sdr = OutputDescriptions.Sdr(colorimetry?.Chromaticities);
        }
        else if (state.SdrGamutWideness > 0 && colorimetry?.Chromaticities is { } native)
        {
            sdr = OutputDescriptions.Sdr(Interpolate(native, state.SdrGamutWideness / 10000.0));
        }
        else
        {
            sdr = ImageDescription.Srgb;
        }

        var headroom = EdrHeadroomOf(output);
        if (headroom > 1.001)
        {
            var reference = (uint)TransferCharacteristics.SdrReferenceLuminance;
            sdr = sdr with
            {
                Luminances = (0, (uint)Math.Round(reference * headroom), reference),
            };
        }

        return sdr;
    }

    public bool IsKmsRouted(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return _routedCtm.ContainsKey(output);
    }

    public bool RouteKmsPipeline(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var state = StateOf(output);
        var description = DescriptionOf(output);
        if (output is not IOutputColorPipeline pipeline ||
            pipeline.GammaLutSize == 0 || pipeline.DegammaLutSize == 0 || !pipeline.SupportsCtm ||
            state.HighDynamicRange)
        {
            UnrouteKmsPipeline(output);
            return false;
        }

        double[] ctm;
        OutputGammaRamps gamma;
        if (description.IccData is { } icc)
        {
            if (!KmsColorPipeline.TryExtractMatrixShaper(icc, (int)pipeline.GammaLutSize, out ctm, out gamma))
            {
                UnrouteKmsPipeline(output);
                return false;
            }
        }
        else
        {
            if (ColorLutBaker.IsIdentity(ImageDescription.Srgb, description) ||
                !KmsColorPipeline.CanExpress(ImageDescription.Srgb, description))
            {
                UnrouteKmsPipeline(output);
                return false;
            }

            ctm = KmsColorPipeline.GamutCtm(ImageDescription.Srgb, description);
            var scale = KmsColorPipeline.HeadroomScale(ImageDescription.Srgb, description);
            if (scale < 1.0)
            {
                for (var i = 0; i < 9; i++)
                {
                    ctm[i] *= scale;
                }
            }

            gamma = KmsColorPipeline.EncodeRamps(description, (int)pipeline.GammaLutSize);
        }

        _routedCtm[output] = ctm;
        _routedGamma[output] = gamma;
        Record(output, state);
        using var commit = new OutputState();
        commit.SetDegammaLut(KmsColorPipeline.DecodeRamps(ImageDescription.Srgb, (int)pipeline.DegammaLutSize));
        commit.SetCtm(CtmFor(output, SoftwareFactor(output, state)));
        commit.SetGammaLut(gamma);
        if (output.Commit(commit))
        {
            return true;
        }

        _routedCtm.Remove(output);
        _routedGamma.Remove(output);
        return false;
    }

    public OutputGammaRamps? RoutedEncodeRamps(IOutput output, (double R, double G, double B)? multipliers = null)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (!_routedGamma.TryGetValue(output, out var ramps))
        {
            return null;
        }

        if (multipliers is not { } m)
        {
            return ramps;
        }

        var size = ramps.Red.Length;
        var scaled = new OutputGammaRamps(new ushort[size], new ushort[size], new ushort[size]);
        for (var i = 0; i < size; i++)
        {
            scaled.Red[i] = (ushort)Math.Clamp(Math.Round(ramps.Red[i] * m.R), 0, ushort.MaxValue);
            scaled.Green[i] = (ushort)Math.Clamp(Math.Round(ramps.Green[i] * m.G), 0, ushort.MaxValue);
            scaled.Blue[i] = (ushort)Math.Clamp(Math.Round(ramps.Blue[i] * m.B), 0, ushort.MaxValue);
        }

        return scaled;
    }

    private void UnrouteKmsPipeline(IOutput output)
    {
        if (!_routedCtm.Remove(output))
        {
            return;
        }

        _routedGamma.Remove(output);

        using var commit = new OutputState();
        commit.SetDegammaLut(null);
        commit.SetCtm(CtmFor(output, SoftwareFactor(output, StateOf(output))));
        commit.SetGammaLut(null);
        _ = output.Commit(commit);
    }

    private double SoftwareFactor(IOutput output, OutputColorState state)
    {
        var hardware = Brightness is { } control && control.Supports(output) && control.Max(output) > 0 &&
            (state.DdcCiAllowed || !control.UsesDdcCi(output));
        return (hardware ? 1.0 : state.Brightness / 10000.0) * (state.Dimming / 10000.0);
    }

    private double[]? CtmFor(IOutput output, double factor)
    {
        if (!_routedCtm.TryGetValue(output, out var matrix))
        {
            return factor >= 1.0 ? null : [factor, 0, 0, 0, factor, 0, 0, 0, factor];
        }

        if (factor >= 1.0)
        {
            return matrix;
        }

        var scaled = new double[9];
        for (var i = 0; i < 9; i++)
        {
            scaled[i] = matrix[i] * factor;
        }

        return scaled;
    }

    private void ApplyColor(in OutputConfigurationEntry entry)
    {
        var output = entry.Output;
        var previous = StateOf(output);
        var next = previous;
        if (entry.HighDynamicRange is { } hdr)
        {
            next = next with { HighDynamicRange = hdr && (Supported(output) & OutputConfigurationFeatures.HighDynamicRange) != 0 };
        }

        if (entry.WideColorGamut is { } wcg)
        {
            next = next with { WideColorGamut = wcg };
        }

        if (entry.SdrBrightnessNits is { } sdr)
        {
            next = next with { SdrBrightnessNits = sdr };
        }

        if (entry.SdrGamutWideness is { } wideness)
        {
            next = next with { SdrGamutWideness = wideness };
        }

        if (entry.ColorProfileSource is { } source)
        {
            next = next with { Source = source };
        }

        if (entry.IccProfilePath is { } icc)
        {
            next = next with { IccProfilePath = icc.Length > 0 ? icc : null };
        }

        if (entry.HdrColorProfileSource is { } hdrSource)
        {
            next = next with { HdrSource = hdrSource };
        }

        if (entry.HdrIccProfilePath is { } hdrIcc)
        {
            next = next with { HdrIccProfilePath = hdrIcc.Length > 0 ? hdrIcc : null };
        }

        if (entry.BrightnessOverrides is { } overrides)
        {
            next = next with { BrightnessOverrides = overrides };
        }

        if (entry.DdcCiAllowed is { } ddcCiAllowed)
        {
            next = next with { DdcCiAllowed = ddcCiAllowed };
        }

        if (entry.EdrPolicy is { } edrPolicy)
        {
            next = next with { EdrPolicy = edrPolicy };
        }

        if (entry.ColorPowerTradeoff is { } tradeoff)
        {
            next = next with { ColorPowerTradeoff = tradeoff };
        }

        if (entry.Brightness is { } brightness)
        {
            next = next with { Brightness = brightness };
        }

        if (entry.Dimming is { } dimming)
        {
            next = next with { Dimming = dimming };
        }

        if (next == previous && _states.ContainsKey(output))
        {
            return;
        }

        Record(output, next);

        if (next.HighDynamicRange != previous.HighDynamicRange ||
            (next.HighDynamicRange && next.BrightnessOverrides != previous.BrightnessOverrides))
        {
            using var state = new OutputState();
            if (next.HighDynamicRange)
            {
                state.SetHdr(OutputDescriptions.HdrMetadataFor(DescriptionOf(output), output.Colorimetry?.Chromaticities));
            }
            else
            {
                state.SetHdr(null);
            }

            _ = output.Commit(state);
        }

        var hardware = Brightness is { } control && control.Supports(output) && control.Max(output) > 0 &&
            (next.DdcCiAllowed || !control.UsesDdcCi(output));
        if (hardware && next.Brightness != previous.Brightness)
        {
            ApplyEdrBacklight(output, next, EdrHeadroomOf(output));
        }

        ReevaluateEdr(output);

        var factor = SoftwareFactor(output, next);
        var previousFactor = SoftwareFactor(output, previous);
        if (Math.Abs(factor - previousFactor) > double.Epsilon)
        {
            using var state = new OutputState();
            state.SetCtm(CtmFor(output, factor));
            _ = output.Commit(state);
        }
    }

    private void Record(IOutput output, OutputColorState state)
    {
        _states[output] = state;
        if (!_cleanup.ContainsKey(output))
        {
            void OnDestroyed()
            {
                _states.Remove(output);
                _routedCtm.Remove(output);
                _routedGamma.Remove(output);
                _cleanup.Remove(output);
            }

            _cleanup[output] = OnDestroyed;
            output.Destroyed += OnDestroyed;
        }
    }

    private bool ValidateProfiles(in OutputConfigurationEntry entry)
    {
        if (entry.IccProfilePath is { Length: > 0 } path && !TryReadProfile(path, out _))
        {
            _failure = $"the icc profile at {path} could not be read";
            return false;
        }

        if (entry.HdrIccProfilePath is { Length: > 0 } hdrPath && !TryReadProfile(hdrPath, out _))
        {
            _failure = $"the icc profile at {hdrPath} could not be read";
            return false;
        }

        return true;
    }

    private static bool TryReadProfile(string path, out byte[] data)
    {
        try
        {
            data = File.ReadAllBytes(path);
        }
        catch (IOException)
        {
            data = [];
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            data = [];
            return false;
        }

        return IccProfiles.Validate(data);
    }

    private static (double Rx, double Ry, double Gx, double Gy, double Bx, double By, double Wx, double Wy) Interpolate(
        (double Rx, double Ry, double Gx, double Gy, double Bx, double By, double Wx, double Wy) native, double amount)
    {
        var srgb = Chromaticities.Srgb;
        double Mix(double from, double to) => from + (to - from) * amount;
        return (
            Mix(srgb.Rx, native.Rx), Mix(srgb.Ry, native.Ry),
            Mix(srgb.Gx, native.Gx), Mix(srgb.Gy, native.Gy),
            Mix(srgb.Bx, native.Bx), Mix(srgb.By, native.By),
            Mix(srgb.Wx, native.Wx), Mix(srgb.Wy, native.Wy));
    }
}
