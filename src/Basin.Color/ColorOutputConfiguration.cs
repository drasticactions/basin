using Basin.Capabilities;

namespace Basin.Color;

/// <summary>
/// Decorates an <see cref="IOutputConfiguration"/> with the color state of each output: HDR, ICC
/// profiles, SDR white, gamut wideness, brightness, EDR and looks. It runs on the compositor thread
/// only.
/// </summary>
public sealed class ColorOutputConfiguration : IOutputConfiguration, IOutputLooks
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

    /// <summary>
    /// Initializes a new instance of the <see cref="ColorOutputConfiguration"/> class. It forwards
    /// layout and mode changes to the inner configuration.
    /// </summary>
    /// <param name="inner">The configuration that applies everything that is not color.</param>
    public ColorOutputConfiguration(IOutputConfiguration inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <summary>
    /// Occurs once after each successful <see cref="Apply"/>, with the entries that went in after
    /// adjustment.
    /// </summary>
    public event Action<IReadOnlyList<OutputConfigurationEntry>>? Applied;

    /// <summary>
    /// Gets or sets the hardware brightness devices. When a device serves an output, brightness goes to
    /// the device and EDR becomes possible. When it is <see langword="null"/>, brightness is a software
    /// CTM.
    /// </summary>
    public IOutputBrightness? Brightness { get; set; }

    /// <summary>
    /// Occurs when the EDR headroom of an output changes, after the backlight is set.
    /// </summary>
    public event Action<IOutput>? EdrChanged;

    /// <summary>
    /// Occurs when something that <see cref="DescriptionOf"/> reads changes outside <see
    /// cref="Apply"/>: the panel entry, the look, the inverse tone mapping, or HDR on demand.
    /// </summary>
    public event Action<IOutput>? DescriptionChanged;

    private readonly Dictionary<IOutput, bool> _hdrDemand = [];

    /// <summary>
    /// Gets the last HDR demand that <see cref="SetHdrDemand"/> reported for an output.
    /// </summary>
    /// <param name="output">The output.</param>
    /// <returns><see langword="false"/> when nothing was reported.</returns>
    public bool HdrDemandOf(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return _hdrDemand.GetValueOrDefault(output);
    }

    /// <summary>
    /// Records whether a window on an output needs HDR. When HDR on demand is set and the output
    /// supports HDR, it applies the HDR mode and raises <see cref="DescriptionChanged"/>.
    /// </summary>
    /// <param name="output">The output.</param>
    /// <param name="demand">Whether a visible window has an HDR description.</param>
    public void SetHdrDemand(IOutput output, bool demand)
    {
        ArgumentNullException.ThrowIfNull(output);
        _hdrDemand[output] = demand;
        var state = StateOf(output);
        if (!state.HdrOnDemand || state.HighDynamicRange == demand ||
            (demand && (Supported(output) & OutputConfigurationFeatures.HighDynamicRange) == 0))
        {
            return;
        }

        if (Apply([new OutputConfigurationEntry { Output = output, Enabled = output.Enabled, HighDynamicRange = demand }]))
        {
            DescriptionChanged?.Invoke(output);
        }
    }

    private readonly Dictionary<IOutput, PanelEntry> _panels = [];

    /// <summary>
    /// Gets the panel table entry that <see cref="SetPanel"/> attached to an output.
    /// </summary>
    /// <param name="output">The output.</param>
    /// <returns>The entry, or <see langword="null"/> when none is attached.</returns>
    public PanelEntry? PanelOf(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return _panels.GetValueOrDefault(output);
    }

    /// <summary>
    /// Gets the colorimetry of an output, with the panel entry's corrections applied over the EDID
    /// values.
    /// </summary>
    /// <param name="output">The output.</param>
    /// <returns>The colorimetry, or <see langword="null"/> when the output reports none and no panel entry adds any.</returns>
    public OutputColorimetry? ColorimetryOf(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return _panels.TryGetValue(output, out var panel) ? panel.Apply(output.Colorimetry) : output.Colorimetry;
    }

    /// <summary>
    /// Attaches or removes the panel table entry of an output. A change raises <see
    /// cref="DescriptionChanged"/>.
    /// </summary>
    /// <param name="output">The output.</param>
    /// <param name="panel">The entry, or <see langword="null"/> to remove it.</param>
    public void SetPanel(IOutput output, PanelEntry? panel)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (panel is null)
        {
            if (!_panels.Remove(output))
            {
                return;
            }
        }
        else
        {
            if (_panels.TryGetValue(output, out var previous) && previous == panel)
            {
                return;
            }

            _panels[output] = panel;
            Record(output, StateOf(output));
        }

        DescriptionChanged?.Invoke(output);
    }

    /// <summary>
    /// Sets whether HDR follows <see cref="SetHdrDemand"/> on an output. Turning it on applies the
    /// current demand at once.
    /// </summary>
    /// <param name="output">The output.</param>
    /// <param name="onDemand">Whether HDR follows the demand.</param>
    public void SetHdrOnDemand(IOutput output, bool onDemand)
    {
        ArgumentNullException.ThrowIfNull(output);
        var state = StateOf(output);
        if (state.HdrOnDemand == onDemand)
        {
            return;
        }

        Record(output, state with { HdrOnDemand = onDemand });
        if (onDemand)
        {
            SetHdrDemand(output, HdrDemandOf(output));
        }
    }

    /// <summary>
    /// Sets the SDR to HDR expansion that the output description carries while HDR is on. A change
    /// raises <see cref="DescriptionChanged"/>.
    /// </summary>
    /// <param name="output">The output.</param>
    /// <param name="mapping">The expansion, or <see langword="null"/> for none.</param>
    public void SetInverseToneMapping(IOutput output, InverseToneMapping? mapping)
    {
        ArgumentNullException.ThrowIfNull(output);
        var state = StateOf(output);
        if (state.InverseToneMapping == mapping)
        {
            return;
        }

        Record(output, state with { InverseToneMapping = mapping });
        DescriptionChanged?.Invoke(output);
    }

    private readonly Dictionary<IOutput, string> _lookPaths = [];

    /// <inheritdoc/>
    public string? LookOf(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return _lookPaths.GetValueOrDefault(output);
    }

    /// <summary>
    /// Loads a <c>.cube</c> file as the look of an output. On failure the current look stays.
    /// </summary>
    /// <param name="output">The output.</param>
    /// <param name="path">The file path, or <see langword="null"/> to remove the look.</param>
    /// <param name="error">The path and the reason when the file does not load, otherwise <see langword="null"/>.</param>
    /// <returns><see langword="false"/> when the file cannot be read or parsed.</returns>
    public bool SetLook(IOutput output, string? path, out string? error)
    {
        ArgumentNullException.ThrowIfNull(output);
        error = null;
        if (path is null)
        {
            _lookPaths.Remove(output);
            SetLook(output, null);
            return true;
        }

        ColorLut3D look;
        try
        {
            look = CubeLut.Parse(File.ReadAllBytes(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException)
        {
            error = $"{path}: {exception.Message}";
            return false;
        }

        _lookPaths[output] = path;
        SetLook(output, look);
        return true;
    }

    /// <summary>
    /// Sets the look table of an output. A different table raises <see cref="DescriptionChanged"/>.
    /// </summary>
    /// <param name="output">The output.</param>
    /// <param name="look">The table, or <see langword="null"/> to remove the look.</param>
    public void SetLook(IOutput output, ColorLut3D? look)
    {
        ArgumentNullException.ThrowIfNull(output);
        var state = StateOf(output);
        if (ReferenceEquals(state.Look, look))
        {
            return;
        }

        Record(output, state with { Look = look });
        DescriptionChanged?.Invoke(output);
    }

    private const double ZeroBrightnessLuminance = 0.04;
    private const double MaxEdrHeadroom = 3.0;
    private readonly Dictionary<IOutput, double> _edrDemand = [];
    private readonly Dictionary<IOutput, double> _edrHeadroom = [];

    /// <summary>
    /// Gets the EDR headroom in effect on an output, as the ratio of peak to SDR white.
    /// </summary>
    /// <param name="output">The output.</param>
    /// <returns>The headroom, which is 1 when EDR is off.</returns>
    public double EdrHeadroomOf(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return _edrHeadroom.GetValueOrDefault(output, 1.0);
    }

    /// <summary>
    /// Records the headroom that the content on an output asks for. The headroom in effect is clamped
    /// by the brightness and the panel, and only applies while the EDR policy is always.
    /// </summary>
    /// <param name="output">The output.</param>
    /// <param name="demand">The largest ratio of peak to reference luminance among the visible surfaces. 1 means no demand.</param>
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

    /// <inheritdoc/>
    public string? LastFailureReason => _failure ?? _inner.LastFailureReason;

    /// <summary>
    /// Gets the properties that an output can set. It adds the color features that the output reports,
    /// brightness, DDC/CI and EDR, and the panel entry's HDR override.
    /// </summary>
    /// <param name="output">The output.</param>
    /// <returns>The available properties.</returns>
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

        if (_panels.TryGetValue(output, out var panel) && panel.Hdr is { } hdr)
        {
            features = hdr
                ? features | OutputConfigurationFeatures.HighDynamicRange
                : features & ~OutputConfigurationFeatures.HighDynamicRange;
        }

        return features;
    }

    /// <summary>
    /// Checks that every named ICC profile reads and validates, then tests the inner configuration.
    /// </summary>
    /// <param name="entries">The configuration to check.</param>
    /// <returns><see langword="true"/> when every entry can commit. <see cref="LastFailureReason"/> tells why one cannot.</returns>
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

    /// <summary>
    /// Applies a configuration through the inner configuration, then commits the color state of each
    /// entry. The accuracy power tradeoff clears the ABM level of an entry first.
    /// </summary>
    /// <param name="entries">The configuration to apply.</param>
    /// <returns><see langword="true"/> when the configuration is in effect. <see cref="Applied"/> is raised then.</returns>
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

    /// <summary>
    /// Reads the committed configuration of an output, with the recorded color state over what the
    /// inner configuration reports.
    /// </summary>
    /// <param name="output">The output.</param>
    /// <param name="state">The configuration. Without inner state, it holds the output and its enabled flag plus the color state.</param>
    /// <returns>Always <see langword="true"/>.</returns>
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

    /// <summary>
    /// Records a color state for an output without committing anything or raising an event. Use it to
    /// restore saved settings before the first apply.
    /// </summary>
    /// <param name="output">The output.</param>
    /// <param name="state">The state to record.</param>
    public void Seed(IOutput output, OutputColorState state)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(state);
        Record(output, state);
    }

    /// <summary>
    /// Gets the recorded color state of an output.
    /// </summary>
    /// <param name="output">The output.</param>
    /// <returns>The state, or a default <see cref="OutputColorState"/> when nothing is recorded.</returns>
    public OutputColorState StateOf(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return _states.GetValueOrDefault(output) ?? new OutputColorState();
    }

    /// <summary>
    /// Builds the image description that an output displays now, from its color state, colorimetry,
    /// panel entry, EDR headroom and look. The renderer converts surfaces into it.
    /// </summary>
    /// <param name="output">The output.</param>
    /// <returns>A new description. HDR gives PQ, an ICC source gives the profile, and otherwise it is SDR with the selected primaries.</returns>
    public ImageDescription DescriptionOf(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var state = StateOf(output);
        var colorimetry = ColorimetryOf(output);
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

            return state.InverseToneMapping is null && state.Look is null
                ? description
                : description with { InverseToneMapping = state.InverseToneMapping, Look = state.Look };
        }

        if (state.Source == OutputColorProfileSource.Icc && state.IccProfilePath is { } path &&
            TryReadProfile(path, out var icc))
        {
            return new ImageDescription { IccData = icc, Look = state.Look };
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
            sdr = ImageDescription.SdrDefault;
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

        if (_panels.TryGetValue(output, out var curvePanel) && curvePanel.OutputCurve is { } curve)
        {
            sdr = sdr with { TransferNamed = curve };
        }

        return state.Look is null ? sdr : sdr with { Look = state.Look };
    }

    /// <summary>
    /// Tells whether the output transform of an output runs in the CRTC color pipeline instead of
    /// surface tables.
    /// </summary>
    /// <param name="output">The output.</param>
    /// <returns><see langword="true"/> after a successful <see cref="RouteKmsPipeline"/> until a refused one.</returns>
    public bool IsKmsRouted(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return _routedCtm.ContainsKey(output);
    }

    /// <summary>
    /// Tries to move the output transform into the CRTC degamma, CTM and gamma stages, and commits
    /// them. A refusal removes any earlier route.
    /// </summary>
    /// <param name="output">The output.</param>
    /// <returns><see langword="false"/> when the output has no such pipeline, the description is HDR or needs a table, the stages cannot express it, or the commit fails.</returns>
    public bool RouteKmsPipeline(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var state = StateOf(output);
        var description = DescriptionOf(output);
        if (output is not IOutputColorPipeline pipeline ||
            pipeline.GammaLutSize == 0 || pipeline.DegammaLutSize == 0 || !pipeline.SupportsCtm ||
            state.HighDynamicRange || description.NeedsBakedTable)
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
            if (ColorLutBaker.IsIdentity(ImageDescription.SdrDefault, description) ||
                !KmsColorPipeline.CanExpress(ImageDescription.SdrDefault, description))
            {
                UnrouteKmsPipeline(output);
                return false;
            }

            ctm = KmsColorPipeline.GamutCtm(ImageDescription.SdrDefault, description);
            var scale = KmsColorPipeline.HeadroomScale(ImageDescription.SdrDefault, description);
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
        commit.SetDegammaLut(KmsColorPipeline.DecodeRamps(ImageDescription.SdrDefault, (int)pipeline.DegammaLutSize));
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

    /// <summary>
    /// Gets the encode ramps that the route committed to an output, scaled per channel when multipliers
    /// are given.
    /// </summary>
    /// <param name="output">The output.</param>
    /// <param name="multipliers">The per-channel scale, for example for night light, or <see langword="null"/> for the ramps as committed.</param>
    /// <returns>New ramps when scaled, the committed ramps otherwise, or <see langword="null"/> when the output is not routed.</returns>
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
                state.SetHdr(OutputDescriptions.HdrMetadataFor(DescriptionOf(output), ColorimetryOf(output)?.Chromaticities));
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
                _edrDemand.Remove(output);
                _edrHeadroom.Remove(output);
                _hdrDemand.Remove(output);
                _panels.Remove(output);
                _lookPaths.Remove(output);
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
