using Basin.Diagnostics;
using Drm;
using Drm.Native;
using Liftoff;
using static Basin.Backend.Drm.DrmLog;

namespace Basin.Backend.Drm;

public sealed unsafe class DrmOutput : OutputBase, IHardwareCursor, IPresentingOutput, Basin.Capabilities.IOutputColorPipeline
{
    private readonly DrmBackend _backend;
    private readonly uint _connectorId;
    private readonly long[] _outFenceSlot = new long[1];
    private readonly System.Runtime.InteropServices.GCHandle _outFencePin;

    public int LastOutFenceFd { get; private set; } = -1;

    public bool LastFlipWasAsync { get; private set; }

    private readonly DrmPropertyMap _connectorProps;
    private readonly DrmPropertyMap _crtcProps;
    private readonly DrmPropertyMap _planeProps;
    private readonly DrmPropertyMap? _cursorProps;
    private readonly List<DrmModeInfo> _nativeModes;
    private readonly List<OutputMode> _modes;

    private readonly DrmAtomicBuilder _builder = new();

    private readonly DrmAtomicBuilder _testBuilder = new();
    private readonly List<LiftoffLayer> _liftoffLayers = [];
    private readonly List<uint> _layerFbIds = [];
    private LiftoffOutput? _liftoffOutput;
    private bool _liftoffActive;

    private List<BufferLock> _layerScanout = [];
    private List<BufferLock> _pendingLayerScanout = [];
    private bool _layerFlipPending;

    private uint _modeBlobId;
    private DrmModeInfo _committedMode;
    private bool _hardwareLit;
    private bool _needsModeset = true;
    private bool _flipPending;
    private BufferLock _scanout;
    private BufferLock _pendingScanout;

    private BufferLock _cursorBuffer;
    private BufferLock _cursorScanout;
    private BufferLock _pendingCursorScanout;
    private bool _cursorFlipPending;
    private int _cursorX, _cursorY, _hotspotX, _hotspotY;
    private bool _cursorVisible;
    private bool _cursorDirty;

    internal DrmOutput(
        DrmBackend backend,
        DrmConnector connector,
        uint crtcId,
        int crtcIndex,
        uint planeId,
        uint? cursorPlaneId)
        : base(connector.Name)
    {
        _backend = backend;
        _connectorId = connector.ConnectorId;
        CrtcId = crtcId;
        CrtcIndex = crtcIndex;
        PlaneId = planeId;
        CursorPlaneId = cursorPlaneId;
        _connectorProps = new DrmPropertyMap(backend.Device, _connectorId, DrmObjectType.Connector);
        _crtcProps = new DrmPropertyMap(backend.Device, crtcId, DrmObjectType.Crtc);
        _planeProps = new DrmPropertyMap(backend.Device, planeId, DrmObjectType.Plane);
        _outFencePin = System.Runtime.InteropServices.GCHandle.Alloc(_outFenceSlot, System.Runtime.InteropServices.GCHandleType.Pinned);
        _cursorProps = cursorPlaneId is { } cursor ? new DrmPropertyMap(backend.Device, cursor, DrmObjectType.Plane) : null;

        GammaLutSize = _crtcProps.TryGetValue("GAMMA_LUT_SIZE", out var gammaSize) ? (uint)gammaSize : 0;
        DegammaLutSize = _crtcProps.TryGetValue("DEGAMMA_LUT_SIZE", out var degammaSize) ? (uint)degammaSize : 0;
        _nativeModes = [.. connector.Modes];
        _modes = [.. _nativeModes.Select(ToOutputMode)];
        var preferredIndex = Math.Max(0, _nativeModes.FindIndex(m => m.IsPreferred));
        PreferredMode = _modes.Count > 0 ? _modes[preferredIndex] : default;
        PhysicalSize = ((int)connector.WidthMm, (int)connector.HeightMm);
        Class = connector.Type is DrmConnectorType.Edp or DrmConnectorType.Lvds or DrmConnectorType.Dsi
            ? OutputClass.Handheld
            : OutputClass.Desktop;
        ScanoutFormats = ReadInFormats(backend.Device, _planeProps);
        OverlayScanoutFormats = backend.OverlayFormatsFor(crtcIndex);

        MaxBitsPerColorRange = ReadMaxBpcRange();
        var edidBytes = ReadEdidBytes();
        _edidBytes = edidBytes;
        var edid = edidBytes.Length > 0 ? EdidInfo.Parse(edidBytes) : new EdidInfo("unknown", "unknown", string.Empty);
        Edid = edid;
        _wideColorGamutCapable = _connectorProps.Has("Colorspace") &&
            (TryEnumValue("Colorspace", "BT2020_RGB", out _) || TryEnumValue("Colorspace", "BT2020_YCC", out _)) &&
            edid.SupportsBt2020 && VendorAllowsColorspace();
        _highDynamicRangeCapable = _connectorProps.Has("HDR_OUTPUT_METADATA") && edid.SupportsPq && _wideColorGamutCapable;
        Make = edid.Make;
        Model = edid.Model;
        Serial = edid.Serial;
        Description = $"{edid.Make} {edid.Model} ({connector.Name})";
        _cursorRetry = backend.Loop.AddTimer(OnCursorRetry);
        _queuedFlush = backend.Loop.AddTimer(OnQueuedFlush);
        if (backend.Liftoff is { } liftoff)
        {
            try
            {
                _liftoffOutput = liftoff.CreateOutput(crtcId);
            }
            catch (LiftoffException e)
            {
                _liftoffOutput = null;
                Log.Warn($"{Name}: liftoff output unavailable ({e.Message}); overlay offload disabled");
            }
        }
    }

    internal uint CrtcId { get; }

    internal int CrtcIndex { get; }

    internal uint PlaneId { get; }

    internal uint? CursorPlaneId { get; }

    public IReadOnlyList<OutputMode> Modes => _modes;

    public OutputMode PreferredMode { get; }

    public override OutputClass Class { get; }

    public DrmFormatSet ScanoutFormats { get; }

    public DrmFormatSet OverlayScanoutFormats { get; }

    public override bool CanScanout(DrmFormat format, ulong modifier, bool overlay)
    {
        var formats = overlay ? OverlayScanoutFormats : ScanoutFormats;
        return formats.Count == 0 || formats.Contains(format, modifier);
    }

    public uint GammaLutSize { get; }

    public uint DegammaLutSize { get; }

    public bool SupportsCtm => _crtcProps.Has("CTM");

    public EdidInfo Edid { get; }

    public override ReadOnlyMemory<byte> EdidBytes => _edidBytes;

    public (uint Min, uint Max) MaxBitsPerColorRange { get; }

    public event Action<ulong, uint, ulong>? PresentedOnScreen;

    public override void RequestFrame()
    {
        if (!_flipPending && _backend.SessionActive && Enabled)
        {
            EmitFrame();
        }
    }

    protected override bool SupportsLayers => _liftoffOutput is not null;

    protected override bool SupportsAdaptiveSync =>
        _crtcProps.Has("VRR_ENABLED") && _connectorProps.TryGetValue("vrr_capable", out var capable) && capable != 0;

    protected override bool SupportsRgbRange => _connectorProps.Has("Broadcast RGB");

    protected override bool SupportsMaxBitsPerColor => _connectorProps.Has("max bpc");

    protected override bool SupportsOverscan =>
        _connectorProps.Has("overscan") ||
        (_connectorProps.Has("underscan") &&
         _connectorProps.Has("underscan vborder") &&
         _connectorProps.Has("underscan hborder"));

    protected override bool SupportsCustomModes => Libxcvt.IsAvailable;

    protected override bool SupportsSharpness => _crtcProps.Has("SHARPNESS_STRENGTH");

    protected override bool SupportsAbmLevel => _connectorProps.Has("adaptive backlight modulation");

    public override Basin.Capabilities.OutputConfigurationFeatures Features
    {
        get
        {
            var features = Basin.Capabilities.OutputConfigurationFeatures.None;
            if (SupportsOverscan)
            {
                features |= Basin.Capabilities.OutputConfigurationFeatures.Overscan;
            }

            if (SupportsAdaptiveSync)
            {
                features |= Basin.Capabilities.OutputConfigurationFeatures.Vrr;
            }

            if (SupportsRgbRange)
            {
                features |= Basin.Capabilities.OutputConfigurationFeatures.RgbRange;
            }

            if (SupportsMaxBitsPerColor)
            {
                features |= Basin.Capabilities.OutputConfigurationFeatures.MaxBitsPerColor;
            }

            if (SupportsCustomModes)
            {
                features |= Basin.Capabilities.OutputConfigurationFeatures.CustomModes;
            }

            if (SupportsSharpness)
            {
                features |= Basin.Capabilities.OutputConfigurationFeatures.Sharpness;
            }

            if (SupportsAbmLevel)
            {
                features |= Basin.Capabilities.OutputConfigurationFeatures.AbmLevel;
            }

            features |= Basin.Capabilities.OutputConfigurationFeatures.IccProfile;
            features |= Basin.Capabilities.OutputConfigurationFeatures.HdrIccProfile;
            if (_wideColorGamutCapable)
            {
                features |= Basin.Capabilities.OutputConfigurationFeatures.WideColorGamut;
            }

            if (_highDynamicRangeCapable)
            {
                features |= Basin.Capabilities.OutputConfigurationFeatures.HighDynamicRange;
            }

            if (EdidBytes.Length > 0 && Edid.Chromaticities.HasValue)
            {
                features |= Basin.Capabilities.OutputConfigurationFeatures.BuiltInColor;
            }

            return features;
        }
    }

    public override Basin.Capabilities.OutputColorimetry? Colorimetry =>
        EdidBytes.Length == 0
            ? null
            : new Basin.Capabilities.OutputColorimetry
            {
                MaxLuminance = Edid.MaxLuminance,
                MaxFrameAverageLuminance = Edid.MaxFrameAverageLuminance,
                MinLuminance = Edid.MinLuminance,
                Chromaticities = Edid.Chromaticities,
                SupportsPq = Edid.SupportsPq,
                SupportsBt2020 = Edid.SupportsBt2020,
            };

    public override bool SupportsInFence => _planeProps.Has("IN_FENCE_FD");

    protected override bool TestCommitCore(OutputState state)
    {
        var layers = (state.Fields & OutputStateFields.Layers) != 0 ? state.Layers : null;
        if (layers is not null)
        {
            RejectLayers(layers);
        }

        if ((state.Fields & OutputStateFields.CustomModes) != 0 && state.CustomModes is { } customModes &&
            !CustomModesGenerate(customModes))
        {
            return false;
        }

        if ((state.Fields & OutputStateFields.Mode) != 0 && FindNativeMode(state.Mode) is null &&
            !PendingCustomModeMatches(state, state.Mode))
        {
            return false;
        }

        if ((state.Fields & OutputStateFields.Buffer) != 0 && state.Buffer is { } buffer)
        {
            if (!Enabled && ((state.Fields & OutputStateFields.Enabled) == 0 || !state.Enabled))
            {
                return false;
            }

            var mode = (state.Fields & OutputStateFields.Mode) != 0 ? state.Mode : CurrentMode;
            if (buffer.Width != mode.Width || buffer.Height != mode.Height)
            {
                return false;
            }

            if (_backend.SessionActive && _backend.Framebuffers.GetOrAdd(buffer, true) == 0)
            {
                return false;
            }
        }

        if (layers is not null && CanOffloadLayers(state))
        {
            _ = ApplyLayers(layers, _testBuilder, _liftoffOutput!, _liftoffLayers, out _);
            ReleaseStagedFences();
        }

        return true;
    }

    private bool CanOffloadLayers(OutputState state)
    {
        if (_liftoffOutput is null || !_backend.SessionActive || !_hardwareLit || _needsModeset)
        {
            return false;
        }

        return (state.Fields & OutputStateFields.Mode) == 0 || ModeEquals(state.Mode, _committedMode);
    }

    protected override bool CommitCore(OutputState state)
    {
        if (!_backend.SessionActive)
        {
            _needsModeset = true;
            return true;
        }

        if ((state.Fields & OutputStateFields.Enabled) != 0 && !state.Enabled)
        {
            DisablePipeline();
            return true;
        }

        var modeChanged = (state.Fields & OutputStateFields.Mode) != 0 && !ModeEquals(state.Mode, _committedMode);
        var buffer = (state.Fields & OutputStateFields.Buffer) != 0 ? state.Buffer : null;

        if ((state.Fields & OutputStateFields.GammaLut) != 0)
        {
            _pendingGamma = state.GammaLut;
            _gammaDirty = true;
        }

        if ((state.Fields & OutputStateFields.Ctm) != 0)
        {
            _pendingCtm = state.Ctm;
            _ctmDirty = true;
        }

        if ((state.Fields & OutputStateFields.DegammaLut) != 0)
        {
            _pendingDegamma = state.DegammaLut;
            _degammaDirty = true;
        }

        if ((state.Fields & OutputStateFields.Hdr) != 0)
        {
            if (!Equals(state.Hdr, _pendingHdr))
            {
                _needsModeset = true;
            }

            _pendingHdr = state.Hdr;
            _hdrDirty = true;
        }

        if ((state.Fields & OutputStateFields.RgbRange) != 0 && state.RgbRange != _pendingRgbRange)
        {
            _pendingRgbRange = state.RgbRange;
            _needsModeset = true;
        }

        if ((state.Fields & OutputStateFields.MaxBitsPerColor) != 0 && state.MaxBitsPerColor != _pendingMaxBpc)
        {
            _pendingMaxBpc = state.MaxBitsPerColor;
            _needsModeset = true;
        }

        if ((state.Fields & OutputStateFields.Overscan) != 0 && state.Overscan != _pendingOverscan)
        {
            _pendingOverscan = state.Overscan;
            _needsModeset = true;
        }

        if ((state.Fields & OutputStateFields.CustomModes) != 0 && state.CustomModes is { } customModeList)
        {
            RebuildCustomModes(customModeList);
        }

        if ((state.Fields & OutputStateFields.Sharpness) != 0 && state.Sharpness != _pendingSharpness)
        {
            _pendingSharpness = state.Sharpness;
            _enhancementDirty = true;
        }

        if ((state.Fields & OutputStateFields.AbmLevel) != 0 && state.AbmLevel != _pendingAbmLevel)
        {
            _pendingAbmLevel = state.AbmLevel;
            _needsModeset = true;
        }

        if ((state.Fields & OutputStateFields.AdaptiveSync) != 0 && state.AdaptiveSync != _adaptiveSync)
        {
            _adaptiveSync = state.AdaptiveSync;
            _adaptiveSyncDirty = true;
        }

        if (!Enabled)
        {
            return true;
        }

        var colorDirty = _gammaDirty || _ctmDirty || _degammaDirty || _hdrDirty;
        if (buffer is null && !modeChanged && !_needsModeset && !_cursorDirty && !colorDirty && !_enhancementDirty && !_adaptiveSyncDirty)
        {
            return true;
        }

        var modeset = _needsModeset || modeChanged || !_hardwareLit;
        if (modeset)
        {
            _gammaDirty = _degammaDirty = _ctmDirty = _hdrDirty = true;
            colorDirty = true;
            var native = FindNativeMode((state.Fields & OutputStateFields.Mode) != 0 ? state.Mode : CurrentMode)
                ?? throw new InvalidOperationException($"{Name}: no native mode matches the requested mode");
            ReplaceModeBlob(native);
            _committedMode = native;
        }

        uint fbId = 0;
        if (buffer is not null)
        {
            fbId = _backend.Framebuffers.GetOrAdd(buffer, true);
            if (fbId == 0)
            {
                Log.Warn($"{Name}: buffer not scanout-capable; frame dropped");
                return false;
            }
        }
        else if (_scanout.Buffer is { } current)
        {
            fbId = _backend.Framebuffers.GetOrAdd(current, true);
        }

        if (fbId == 0)
        {
            return true;
        }

        if (_flipPending && !modeset)
        {
            if (buffer is null)
            {
                return true;
            }

            return QueueFrame(state, buffer);
        }

        var builder = _builder;
        builder.Reset();

        var layers = (state.Fields & OutputStateFields.Layers) != 0 ? state.Layers : null;
        if (layers is null && state != _queuedFrame && _queuedFrame is { Fields: not 0 } superseded &&
            (superseded.Fields & OutputStateFields.Layers) != 0)
        {
            layers = superseded.Layers;
        }

        var layersDirty = false;
        if (_liftoffOutput is not null)
        {
            if (modeset)
            {
                if (layers is not null)
                {
                    RejectLayers(layers);
                }

                layersDirty = DisableActiveLayers(builder, allowModeset: true);
                layers = null;
            }
            else if (layers is not null)
            {
                if (ApplyLayers(layers, builder, _liftoffOutput, _liftoffLayers, out var anyAccepted))
                {
                    _liftoffActive |= anyAccepted;
                    layersDirty = true;
                }
                else
                {
                    RejectLayers(layers);
                    builder.Reset();
                    ReleaseStagedFences();
                    layersDirty = DisableActiveLayers(builder, allowModeset: false);
                    layers = null;
                }
            }

        }

        if (modeset)
        {
            builder.Add(_connectorProps, "connector", "CRTC_ID", CrtcId);
            builder.Add(_crtcProps, "crtc", "MODE_ID", _modeBlobId);
            builder.Add(_crtcProps, "crtc", "ACTIVE", 1);
            AddConnectorPreferences(builder);
        }

        AddPlaneProperties(builder, fbId, _committedMode);
        if ((state.Fields & OutputStateFields.InFence) != 0 && state.InFenceFd >= 0 && _planeProps.Has("IN_FENCE_FD"))
        {
            builder.Add(_planeProps, "plane", "IN_FENCE_FD", unchecked((ulong)(long)StageFence(state.InFenceFd)));
        }

        var wantOutFence = (state.Fields & OutputStateFields.OutFence) != 0 && _crtcProps.Has("OUT_FENCE_PTR");
        if (wantOutFence)
        {
            _outFenceSlot[0] = -1;
            builder.Add(_crtcProps, "crtc", "OUT_FENCE_PTR", (ulong)_outFencePin.AddrOfPinnedObject());
        }

        var cursorStaged = _cursorDirty && _cursorProps is not null;
        if (cursorStaged)
        {
            AddCursorProperties(builder);
        }

        if ((_adaptiveSyncDirty || modeset) && _crtcProps.Has("VRR_ENABLED"))
        {
            builder.Add(_crtcProps, "crtc", "VRR_ENABLED", _adaptiveSync ? 1u : 0u);
        }

        if (colorDirty)
        {
            AddColorProperties(builder);
        }

        if (_enhancementDirty || modeset)
        {
            AddEnhancementProperties(builder);
            _enhancementDirty = false;
        }

        var flags = modeset
            ? DrmAtomicCommitFlags.AllowModeset | DrmAtomicCommitFlags.PageFlipEvent
            : DrmAtomicCommitFlags.Nonblock | DrmAtomicCommitFlags.PageFlipEvent;
        var tearing = (state.Fields & OutputStateFields.Tearing) != 0 && state.Tearing && !modeset;
        if (tearing)
        {
            flags |= DrmAtomicCommitFlags.PageFlipAsync;
        }

        if (!Commit(builder, flags, modeset ? "modeset" : "flip"))
        {
            if (!tearing || !Commit(builder, flags & ~DrmAtomicCommitFlags.PageFlipAsync, "flip"))
            {
                ReleaseStagedFences();
                return false;
            }

            tearing = false;
        }

        ReleaseStagedFences();

        LastFlipWasAsync = tearing;

        if (layersDirty)
        {
            foreach (var held in _pendingLayerScanout)
            {
                held.Dispose();
            }

            _pendingLayerScanout.Clear();
            if (layers is not null)
            {
                for (var i = 0; i < layers.Count; i++)
                {
                    if (layers[i].Accepted && layers[i].Buffer is { } layerBuffer)
                    {
                        _pendingLayerScanout.Add(layerBuffer.Lock());
                    }
                }
            }

            _layerFlipPending = true;
        }

        LastOutFenceFd = wantOutFence ? (int)_outFenceSlot[0] : -1;
        if (colorDirty)
        {
            ClearColorDirty();
        }

        if (cursorStaged)
        {
            _pendingCursorScanout.Dispose();
            _pendingCursorScanout = _cursorVisible && _cursorBuffer.Buffer is { } cursorOnPlane ? cursorOnPlane.Lock() : default;
            _cursorFlipPending = true;
        }

        _cursorDirty = false;
        _cursorAwaitingFrame = false;
        _adaptiveSyncDirty = false;
        _needsModeset = false;
        _hardwareLit = true;
        _flipPending = true;
        _lastFrameCommitTick = Environment.TickCount64;
        if (buffer is not null)
        {
            _pendingScanout.Dispose();
            _pendingScanout = buffer.Lock();
            if (state != _queuedFrame)
            {
                DropQueuedFrame();
            }
        }

        return true;
    }

    private OutputState? _queuedFrame;
    private BufferLock _queuedFrameLock;
    private readonly List<OutputLayer> _queuedLayers = [];
    private readonly List<OutputLayer> _queuedLayerPool = [];
    private readonly List<BufferLock> _queuedLayerLocks = [];
    private bool _queuedLayersHeld;
    private int _queuedFenceFd = -1;
    private long _lastFrameCommitTick;

    private bool QueueFrame(OutputState state, IBuffer buffer)
    {
        var fields = state.Fields;
        var tearing = state.Tearing;
        var adaptiveSync = state.AdaptiveSync;
        var fence = -1;
        if ((fields & OutputStateFields.InFence) != 0 && state.InFenceFd >= 0)
        {
            fence = RenderFences.DuplicateFence(state.InFenceFd);
            if (fence < 0)
            {
                return false;
            }
        }

        Log.Debug($"{Name}: frame queued behind pending flip");
        _lastFrameCommitTick = Environment.TickCount64;
        _queuedFlush?.UpdateTimer((int)(2 * RefreshMs()));
        ReleaseQueuedFence();
        (_queuedFrame ??= new OutputState()).Clear();
        _queuedFrame.SetBuffer(buffer);
        if ((fields & OutputStateFields.Tearing) != 0)
        {
            _queuedFrame.SetTearing(tearing);
        }

        if ((fields & OutputStateFields.AdaptiveSync) != 0)
        {
            _queuedFrame.SetAdaptiveSync(adaptiveSync);
        }

        if (fence >= 0)
        {
            _queuedFenceFd = fence;
            _queuedFrame.SetInFence(fence);
        }

        if ((fields & OutputStateFields.Layers) != 0 && state.Layers is { } layerList)
        {
            ReleaseQueuedLayers();
            while (_queuedLayerPool.Count < layerList.Count)
            {
                _queuedLayerPool.Add(new OutputLayer());
            }

            for (var i = 0; i < layerList.Count; i++)
            {
                var source = layerList[i];
                var copy = _queuedLayerPool[i];
                copy.Buffer = source.Buffer;
                copy.SrcBox = source.SrcBox;
                copy.DstBox = source.DstBox;
                copy.Alpha = source.Alpha;
                copy.Opaque = source.Opaque;
                copy.Accepted = false;
                copy.InFenceFd = source.InFenceFd >= 0 ? RenderFences.DuplicateFence(source.InFenceFd) : -1;
                if (source.Buffer is { } layerBuffer)
                {
                    _queuedLayerLocks.Add(layerBuffer.Lock());
                }

                _queuedLayers.Add(copy);
            }

            _queuedLayersHeld = true;
            _queuedFrame.SetLayers(_queuedLayers);
        }
        else if (_queuedLayersHeld)
        {
            _queuedFrame.SetLayers(_queuedLayers);
        }

        _queuedFrameLock.Dispose();
        _queuedFrameLock = buffer.Lock();
        return true;
    }

    private void ReleaseQueuedLayers()
    {
        for (var i = 0; i < _queuedLayers.Count; i++)
        {
            RenderFences.CloseFence(_queuedLayers[i].InFenceFd);
            _queuedLayers[i].InFenceFd = -1;
            _queuedLayers[i].Buffer = null;
        }

        _queuedLayers.Clear();
        foreach (var held in _queuedLayerLocks)
        {
            held.Dispose();
        }

        _queuedLayerLocks.Clear();
        _queuedLayersHeld = false;
    }

    private void ReleaseQueuedFence()
    {
        RenderFences.CloseFence(_queuedFenceFd);
        _queuedFenceFd = -1;
    }

    private void SubmitQueuedFrame()
    {
        if (_queuedFrame is null || _queuedFrame.Fields == 0)
        {
            return;
        }

        _ = CommitCore(_queuedFrame);
        _queuedFrame.Clear();
        ReleaseQueuedFence();
        ReleaseQueuedLayers();
        _queuedFrameLock.Dispose();
        _queuedFrameLock = default;
    }

    private void DropQueuedFrame()
    {
        _queuedFlush?.UpdateTimer(0);
        _queuedFrame?.Clear();
        ReleaseQueuedFence();
        ReleaseQueuedLayers();
        _queuedFrameLock.Dispose();
        _queuedFrameLock = default;
    }

    private IEventSource? _queuedFlush;

    private void OnQueuedFlush()
    {
        if (_queuedFrame is null || _queuedFrame.Fields == 0)
        {
            return;
        }

        if (_flipPending)
        {
            _queuedFlush?.UpdateTimer((int)RefreshMs());
            return;
        }

        SubmitQueuedFrame();
    }

    private long RefreshMs() => Math.Max((long)(RefreshIntervalNs(_committedMode) / 1_000_000), 5);

    private uint _hdrBlobId;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 1)]
    private struct HdrOutputMetadataBlob
    {
        public uint MetadataType;
        public byte Eotf;
        public byte InfoframeType;
        public ushort Red0;
        public ushort Red1;
        public ushort Green0;
        public ushort Green1;
        public ushort Blue0;
        public ushort Blue1;
        public ushort White0;
        public ushort White1;
        public ushort MaxMasteringLuminance;
        public ushort MinMasteringLuminance;
        public ushort MaxCll;
        public ushort MaxFall;

        public ushort Padding;
    }

    private unsafe void ReplaceHdrBlob(HdrStaticMetadata? metadata)
    {
        if (_hdrBlobId != 0)
        {
            _ = Libdrm.drmModeDestroyPropertyBlob(_backend.Device.Fd, _hdrBlobId);
            _hdrBlobId = 0;
        }

        if (metadata is not { } hdr)
        {
            return;
        }

        var blob = new HdrOutputMetadataBlob
        {
            MetadataType = 0,
            Eotf = (byte)hdr.Eotf,
            InfoframeType = 0,
            Red0 = hdr.PrimaryRed.X,
            Red1 = hdr.PrimaryRed.Y,
            Green0 = hdr.PrimaryGreen.X,
            Green1 = hdr.PrimaryGreen.Y,
            Blue0 = hdr.PrimaryBlue.X,
            Blue1 = hdr.PrimaryBlue.Y,
            White0 = hdr.WhitePoint.X,
            White1 = hdr.WhitePoint.Y,
            MaxMasteringLuminance = hdr.MaxMasteringLuminance,
            MinMasteringLuminance = hdr.MinMasteringLuminance,
            MaxCll = hdr.MaxContentLightLevel,
            MaxFall = hdr.MaxFrameAverageLightLevel,
        };
        uint blobId;
        if (Libdrm.drmModeCreatePropertyBlob(_backend.Device.Fd, &blob, (nuint)sizeof(HdrOutputMetadataBlob), &blobId) == 0)
        {
            _hdrBlobId = blobId;
        }
    }

    private bool TryEnumValue(string property, string entryName, out ulong value)
    {
        value = 0;
        var propertyId = _connectorProps.IdOf(property);
        var info = _backend.Device.GetProperty(propertyId);
        foreach (var entry in info.EnumEntries)
        {
            if (entry.Name == entryName)
            {
                value = entry.Value;
                return true;
            }
        }

        return false;
    }

    private static readonly bool AllowIntelColorspace =
        Environment.GetEnvironmentVariable("BASIN_DRM_ALLOW_INTEL_COLORSPACE") == "1";

    private static readonly bool AllowNvidiaColorspace =
        Environment.GetEnvironmentVariable("BASIN_DRM_ALLOW_NVIDIA_COLORSPACE") == "1";

    private readonly byte[] _edidBytes;
    private readonly bool _wideColorGamutCapable;
    private readonly bool _highDynamicRangeCapable;

    private bool VendorAllowsColorspace()
    {
        var name = _backend.Device.GetVersion().Name;
        if (name == "i915")
        {
            return AllowIntelColorspace || Environment.OSVersion.Version >= new Version(6, 11);
        }

        if (name == "nvidia-drm")
        {
            return AllowNvidiaColorspace;
        }

        return true;
    }

    private Basin.Capabilities.OutputRgbRange _pendingRgbRange;
    private uint _pendingMaxBpc;
    private uint _pendingOverscan;

    private void AddConnectorPreferences(DrmAtomicBuilder builder)
    {
        if (_connectorProps.Has("Broadcast RGB") &&
            TryEnumValue("Broadcast RGB", BroadcastRgbName(_pendingRgbRange), out var broadcast))
        {
            builder.Add(_connectorProps, "connector", "Broadcast RGB", broadcast);
        }

        if (_connectorProps.Has("max bpc"))
        {
            var (min, max) = MaxBitsPerColorRange;
            var maxBpc = Math.Clamp(_pendingMaxBpc == 0 ? 10 : _pendingMaxBpc, min, max);
            builder.Add(_connectorProps, "connector", "max bpc", maxBpc);
        }

        if (_connectorProps.Has("adaptive backlight modulation") &&
            TryEnumValue("adaptive backlight modulation", AbmLevelNames[Math.Min(_pendingAbmLevel, 4)], out var abm))
        {
            builder.Add(_connectorProps, "connector", "adaptive backlight modulation", abm);
        }

        if (_connectorProps.Has("overscan"))
        {
            builder.Add(_connectorProps, "connector", "overscan", _pendingOverscan);
        }
        else if (SupportsOverscan)
        {
            var aspect = CurrentMode.Height > 0 ? (double)CurrentMode.Width / CurrentMode.Height : 1.0;
            var vborder = _pendingOverscan;
            var hborder = (uint)(vborder * aspect);
            if (hborder > 128)
            {
                vborder = (uint)(128 / aspect);
                hborder = 128;
            }

            if (TryEnumValue("underscan", vborder != 0 ? "on" : "off", out var underscan))
            {
                builder.Add(_connectorProps, "connector", "underscan", underscan);
                builder.Add(_connectorProps, "connector", "underscan vborder", vborder);
                builder.Add(_connectorProps, "connector", "underscan hborder", hborder);
            }
        }
    }

    private uint _pendingSharpness;
    private uint _pendingAbmLevel;
    private bool _enhancementDirty;
    private bool _adaptiveSync;
    private bool _adaptiveSyncDirty;

    private static readonly string[] AbmLevelNames = ["off", "min", "bias min", "bias max", "max"];

    private void AddEnhancementProperties(DrmAtomicBuilder builder)
    {
        if (_crtcProps.Has("SHARPNESS_STRENGTH"))
        {
            var info = _backend.Device.GetProperty(_crtcProps.IdOf("SHARPNESS_STRENGTH"));
            var max = info.Values.Count >= 2 ? info.Values[1] : 0;
            builder.Add(
                _crtcProps, "crtc", "SHARPNESS_STRENGTH", (ulong)Math.Round(_pendingSharpness * (double)max / 10000.0));
        }

    }

    private static string BroadcastRgbName(Basin.Capabilities.OutputRgbRange range) => range switch
    {
        Basin.Capabilities.OutputRgbRange.Full => "Full",
        Basin.Capabilities.OutputRgbRange.Limited => "Limited 16:235",
        _ => "Automatic",
    };

    private (uint Min, uint Max) ReadMaxBpcRange()
    {
        if (!_connectorProps.Has("max bpc"))
        {
            return (8, 8);
        }

        var info = _backend.Device.GetProperty(_connectorProps.IdOf("max bpc"));
        var values = info.Values;
        return values.Count >= 2 ? ((uint)values[0], (uint)values[1]) : (8, 8);
    }

    private uint _gammaBlobId;
    private uint _degammaBlobId;
    private uint _ctmBlobId;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 2)]
    private struct DrmColorLutEntry
    {
        public ushort Red;
        public ushort Green;
        public ushort Blue;
        public ushort Reserved;
    }

    private OutputGammaRamps? _pendingGamma;
    private OutputGammaRamps? _pendingDegamma;
    private double[]? _pendingCtm;
    private HdrStaticMetadata? _pendingHdr;
    private bool _gammaDirty;
    private bool _ctmDirty;
    private bool _degammaDirty;
    private bool _hdrDirty;

    private void AddColorProperties(DrmAtomicBuilder builder)
    {
        if (_hdrDirty && _connectorProps.Has("HDR_OUTPUT_METADATA"))
        {
            ReplaceHdrBlob(_pendingHdr);
            builder.Add(_connectorProps, "connector", "HDR_OUTPUT_METADATA", _hdrBlobId);
            if (_connectorProps.Has("Colorspace") &&
                TryEnumValue("Colorspace", _pendingHdr is null ? "Default" : "BT2020_RGB", out var colorspace))
            {
                builder.Add(_connectorProps, "connector", "Colorspace", colorspace);
            }
        }

        if (_gammaDirty && _crtcProps.Has("GAMMA_LUT"))
        {
            ReplaceLutBlob(ref _gammaBlobId, _pendingGamma, GammaLutSize);
            builder.Add(_crtcProps, "crtc", "GAMMA_LUT", _gammaBlobId);
        }

        if (_degammaDirty && _crtcProps.Has("DEGAMMA_LUT"))
        {
            ReplaceLutBlob(ref _degammaBlobId, _pendingDegamma, DegammaLutSize);
            builder.Add(_crtcProps, "crtc", "DEGAMMA_LUT", _degammaBlobId);
        }

        if (_ctmDirty && _crtcProps.Has("CTM"))
        {
            ReplaceCtmBlob(_pendingCtm);
            builder.Add(_crtcProps, "crtc", "CTM", _ctmBlobId);
        }
    }

    private void ClearColorDirty()
    {
        _gammaDirty = false;
        _ctmDirty = false;
        _degammaDirty = false;
        _hdrDirty = false;
    }

    private unsafe void ReplaceLutBlob(ref uint blobId, OutputGammaRamps? ramps, uint expectedSize)
    {
        if (blobId != 0)
        {
            _ = Libdrm.drmModeDestroyPropertyBlob(_backend.Device.Fd, blobId);
            blobId = 0;
        }

        if (ramps is not { } lut)
        {
            return;
        }

        if (lut.Red.Length != expectedSize || lut.Green.Length != expectedSize || lut.Blue.Length != expectedSize)
        {
            Log.Warn($"{Name}: gamma ramps sized {lut.Red.Length}, CRTC takes {expectedSize}; ignored");
            return;
        }

        var entries = new DrmColorLutEntry[expectedSize];
        for (var i = 0; i < entries.Length; i++)
        {
            entries[i].Red = lut.Red[i];
            entries[i].Green = lut.Green[i];
            entries[i].Blue = lut.Blue[i];
        }

        fixed (DrmColorLutEntry* data = entries)
        {
            uint created;
            if (Libdrm.drmModeCreatePropertyBlob(
                    _backend.Device.Fd, data, (nuint)(entries.Length * sizeof(DrmColorLutEntry)), &created) == 0)
            {
                blobId = created;
            }
        }
    }

    private unsafe void ReplaceCtmBlob(double[]? matrix)
    {
        if (_ctmBlobId != 0)
        {
            _ = Libdrm.drmModeDestroyPropertyBlob(_backend.Device.Fd, _ctmBlobId);
            _ctmBlobId = 0;
        }

        if (matrix is null)
        {
            return;
        }

        var fixedPoint = stackalloc ulong[9];
        for (var i = 0; i < 9; i++)
        {
            var value = matrix[i];
            var magnitude = (ulong)(Math.Abs(value) * (1L << 32));
            fixedPoint[i] = value < 0 ? magnitude | (1ul << 63) : magnitude;
        }

        uint created;
        if (Libdrm.drmModeCreatePropertyBlob(_backend.Device.Fd, fixedPoint, 72, &created) == 0)
        {
            _ctmBlobId = created;
        }
    }

    private void DestroyColorBlobs()
    {
        if (_gammaBlobId != 0)
        {
            Libdrm.drmModeDestroyPropertyBlob(_backend.Device.Fd, _gammaBlobId);
            _gammaBlobId = 0;
        }

        if (_degammaBlobId != 0)
        {
            Libdrm.drmModeDestroyPropertyBlob(_backend.Device.Fd, _degammaBlobId);
            _degammaBlobId = 0;
        }

        if (_ctmBlobId != 0)
        {
            Libdrm.drmModeDestroyPropertyBlob(_backend.Device.Fd, _ctmBlobId);
            _ctmBlobId = 0;
        }
    }

    private void DisablePipeline()
    {
        var builder = _builder;
        builder.Reset();
        DisableActiveLayers(builder, allowModeset: true);
        builder.Add(_connectorProps, "connector", "CRTC_ID", 0);
        builder.Add(_crtcProps, "crtc", "MODE_ID", 0);
        builder.Add(_crtcProps, "crtc", "ACTIVE", 0);
        builder.Add(_planeProps, "plane", "FB_ID", 0);
        builder.Add(_planeProps, "plane", "CRTC_ID", 0);
        if (_cursorProps is not null)
        {
            builder.Add(_cursorProps, "cursor", "FB_ID", 0);
            builder.Add(_cursorProps, "cursor", "CRTC_ID", 0);
        }

        if (_connectorProps.Has("HDR_OUTPUT_METADATA"))
        {
            builder.Add(_connectorProps, "connector", "HDR_OUTPUT_METADATA", 0);
        }

        if (_connectorProps.Has("Colorspace") && TryEnumValue("Colorspace", "Default", out var colorspace))
        {
            builder.Add(_connectorProps, "connector", "Colorspace", colorspace);
        }

        if (_crtcProps.Has("GAMMA_LUT"))
        {
            builder.Add(_crtcProps, "crtc", "GAMMA_LUT", 0);
        }

        if (_crtcProps.Has("DEGAMMA_LUT"))
        {
            builder.Add(_crtcProps, "crtc", "DEGAMMA_LUT", 0);
        }

        if (_crtcProps.Has("CTM"))
        {
            builder.Add(_crtcProps, "crtc", "CTM", 0);
        }

        Commit(builder, DrmAtomicCommitFlags.AllowModeset, "disable");
        _hardwareLit = false;
        _needsModeset = true;
        _flipPending = false;
        _scanout.Dispose();
        _pendingScanout.Dispose();
        _cursorScanout.Dispose();
        _pendingCursorScanout.Dispose();
        _cursorFlipPending = false;
        ReleaseLayerLocks();
        _backend.Framebuffers.ReleaseOrphans();
    }

    private void AddPlaneProperties(DrmAtomicBuilder builder, uint fbId, DrmModeInfo mode)
    {
        builder.Add(_planeProps, "plane", "FB_ID", fbId);
        builder.Add(_planeProps, "plane", "CRTC_ID", CrtcId);
        builder.Add(_planeProps, "plane", "SRC_X", 0);
        builder.Add(_planeProps, "plane", "SRC_Y", 0);
        builder.Add(_planeProps, "plane", "SRC_W", (ulong)mode.HorizontalDisplay << 16);
        builder.Add(_planeProps, "plane", "SRC_H", (ulong)mode.VerticalDisplay << 16);
        builder.Add(_planeProps, "plane", "CRTC_X", 0);
        builder.Add(_planeProps, "plane", "CRTC_Y", 0);
        builder.Add(_planeProps, "plane", "CRTC_W", mode.HorizontalDisplay);
        builder.Add(_planeProps, "plane", "CRTC_H", mode.VerticalDisplay);
    }

    private bool Commit(DrmAtomicBuilder builder, DrmAtomicCommitFlags flags, string what)
    {
        var t0 = MonotonicClock.Nanos;
        try
        {
            _backend.Device.AtomicCommit(
                builder.Request,
                (flags & ~(DrmAtomicCommitFlags.PageFlipEvent | DrmAtomicCommitFlags.Nonblock)) | DrmAtomicCommitFlags.TestOnly,
                0);
        }
        catch (DrmException e)
        {
            Log.Warn($"{Name}: {what} rejected by TEST_ONLY ({e.Message}); staged properties:\n{builder}");
            return false;
        }

        try
        {
            var t1 = MonotonicClock.Nanos;
            _backend.Device.AtomicCommit(builder.Request, flags, (nint)CrtcId);
            var t2 = MonotonicClock.Nanos;
            Log.Debug($"{what} issue={t2 / 1_000_000} testMs={(t1 - t0) / 1_000_000.0:F1} ioctlMs={(t2 - t1) / 1_000_000.0:F1}");
            return true;
        }
        catch (DrmException e) when (e.Errno == 16 )
        {
            return false;
        }
        catch (DrmException e)
        {
            Log.Warn($"{Name}: {what} failed after TEST_ONLY passed ({e.Message}); requesting a full modeset; staged properties:\n{builder}");
            _needsModeset = true;
            return false;
        }
    }

    internal void OnPageFlip(uint sequence, uint seconds, uint microseconds)
    {
        _flipPending = false;
        if (_pendingScanout.Buffer is not null)
        {
            _scanout.Dispose();
            _scanout = _pendingScanout;
            _pendingScanout = default;
        }

        if (_layerFlipPending)
        {
            (_layerScanout, _pendingLayerScanout) = (_pendingLayerScanout, _layerScanout);
            foreach (var held in _pendingLayerScanout)
            {
                held.Dispose();
            }

            _pendingLayerScanout.Clear();
            _layerFlipPending = false;
        }

        if (_cursorFlipPending)
        {
            _cursorScanout.Dispose();
            _cursorScanout = _pendingCursorScanout;
            _pendingCursorScanout = default;
            _cursorFlipPending = false;
        }

        _backend.Framebuffers.ReleaseOrphans();

        if (_cursorDirty)
        {
            PushCursorState();
        }

        var timeNs = seconds * 1_000_000_000ul + microseconds * 1_000ul;
        Log.Debug($"flip kernel={timeNs / 1_000_000} dispatch={MonotonicClock.Nanos / 1_000_000}");
        PresentedOnScreen?.Invoke(timeNs, RefreshIntervalNs(_committedMode), sequence);
        EmitFrame();
    }

    internal bool IsScanningOut(IBuffer buffer)
    {
        if (ReferenceEquals(_scanout.Buffer, buffer) ||
            ReferenceEquals(_pendingScanout.Buffer, buffer) ||
            ReferenceEquals(_queuedFrameLock.Buffer, buffer) ||
            ReferenceEquals(_cursorBuffer.Buffer, buffer) ||
            ReferenceEquals(_cursorScanout.Buffer, buffer) ||
            ReferenceEquals(_pendingCursorScanout.Buffer, buffer))
        {
            return true;
        }

        foreach (var held in _layerScanout)
        {
            if (ReferenceEquals(held.Buffer, buffer))
            {
                return true;
            }
        }

        foreach (var held in _pendingLayerScanout)
        {
            if (ReferenceEquals(held.Buffer, buffer))
            {
                return true;
            }
        }

        return false;
    }

    internal void OnSessionDisabled()
    {
        _flipPending = false;
        _needsModeset = true;
        DropQueuedFrame();
    }

    internal void OnSessionEnabled()
    {
        _needsModeset = true;
        _cursorDirty = _cursorVisible;
        if (Enabled && _scanout.Buffer is not null)
        {
            using var state = new OutputState();
            Commit(state.SetEnabled(true).SetMode(CurrentMode).SetBuffer(_scanout.Buffer!));
        }

        EmitFrame();
    }

    public bool SetCursor(IBuffer? buffer, int hotspotX, int hotspotY)
    {
        if (_cursorProps is null)
        {
            return false;
        }

        if (buffer is null)
        {
            if (!_cursorVisible)
            {
                return true;
            }

            _cursorBuffer.Dispose();
            _cursorBuffer = default;
            _cursorVisible = false;
            _cursorDirty = true;
            PushCursorState();
            return true;
        }

        var (maxWidth, maxHeight) = _backend.CursorSize;
        if (buffer.Width > maxWidth || buffer.Height > maxHeight)
        {
            return false;
        }

        if (_cursorVisible && ReferenceEquals(_cursorBuffer.Buffer, buffer) && hotspotX == _hotspotX && hotspotY == _hotspotY)
        {
            return true;
        }

        if (_backend.Framebuffers.GetOrAdd(buffer) == 0)
        {
            return false;
        }

        _cursorBuffer.Dispose();
        _cursorBuffer = buffer.Lock();
        _hotspotX = hotspotX;
        _hotspotY = hotspotY;
        _cursorVisible = true;
        _cursorDirty = true;
        PushCursorState();
        return true;
    }

    public void MoveCursor(int x, int y)
    {
        if (x == _cursorX && y == _cursorY)
        {
            return;
        }

        _cursorX = x;
        _cursorY = y;
        if (_cursorVisible)
        {
            _cursorDirty = true;
            PushCursorState();
        }
    }

    private void PushCursorState()
    {
        if (_cursorProps is null || !_backend.SessionActive || !_hardwareLit || _flipPending)
        {
            return;
        }

        var refreshMs = RefreshMs();
        var rideWindow = Math.Max(2 * refreshMs, CursorRideWindowMs);
        var sinceFrame = Environment.TickCount64 - _lastFrameCommitTick;
        if (sinceFrame < rideWindow)
        {
            Log.Debug($"{Name}: cursor rides with the next frame commit");
            _cursorRetry?.UpdateTimer((int)(rideWindow - sinceFrame));
            return;
        }

        if (_backend.CursorRidesWithFrame && _layerScanout.Count > 0)
        {
            Log.Debug($"{Name}: cursor waits for a frame commit while {_layerScanout.Count} layer(s) scan out");
            _cursorAwaitingFrame = true;
            _cursorRetry?.UpdateTimer((int)refreshMs);
            return;
        }

        _cursorAwaitingFrame = false;
        var builder = _builder;
        builder.Reset();
        AddCursorProperties(builder);
        try
        {
            _backend.Device.AtomicCommit(builder.Request, DrmAtomicCommitFlags.Nonblock | DrmAtomicCommitFlags.PageFlipEvent, (nint)CrtcId);
            _pendingCursorScanout.Dispose();
            _pendingCursorScanout = _cursorVisible && _cursorBuffer.Buffer is { } cursorOnPlane ? cursorOnPlane.Lock() : default;
            _cursorFlipPending = true;
            _cursorDirty = false;
            _flipPending = true;
        }
        catch (DrmException)
        {
            _cursorRetry?.UpdateTimer((int)RefreshMs());
        }
    }

    private const long CursorRideWindowMs = 34;

    private IEventSource? _cursorRetry;

    private bool _cursorAwaitingFrame;

    public bool CursorAwaitingFrame => _cursorAwaitingFrame && _cursorDirty;

    internal bool CursorDirty => _cursorDirty;

    private void OnCursorRetry()
    {
        if (_cursorDirty)
        {
            PushCursorState();
        }
    }

    private void AddCursorProperties(DrmAtomicBuilder builder)
    {
        var cursor = _cursorProps!;
        if (!_cursorVisible || _cursorBuffer.Buffer is not { } buffer)
        {
            builder.Add(cursor, "cursor", "FB_ID", 0);
            builder.Add(cursor, "cursor", "CRTC_ID", 0);
            return;
        }

        builder.Add(cursor, "cursor", "FB_ID", _backend.Framebuffers.GetOrAdd(buffer));
        builder.Add(cursor, "cursor", "CRTC_ID", CrtcId);
        builder.Add(cursor, "cursor", "SRC_X", 0);
        builder.Add(cursor, "cursor", "SRC_Y", 0);
        builder.Add(cursor, "cursor", "SRC_W", (ulong)buffer.Width << 16);
        builder.Add(cursor, "cursor", "SRC_H", (ulong)buffer.Height << 16);
        builder.Add(cursor, "cursor", "CRTC_X", unchecked((ulong)(long)(_cursorX - _hotspotX)));
        builder.Add(cursor, "cursor", "CRTC_Y", unchecked((ulong)(long)(_cursorY - _hotspotY)));
        builder.Add(cursor, "cursor", "CRTC_W", (ulong)buffer.Width);
        builder.Add(cursor, "cursor", "CRTC_H", (ulong)buffer.Height);
    }

    [System.Runtime.InteropServices.DllImport("libc")]
    private static extern int dup(int fd);

    [System.Runtime.InteropServices.DllImport("libc")]
    private static extern int close(int fd);

    private readonly List<int> _stagedFences = [];

    private int StageFence(int fenceFd)
    {
        var duplicate = dup(fenceFd);
        if (duplicate >= 0)
        {
            _stagedFences.Add(duplicate);
        }

        return duplicate;
    }

    private void ReleaseStagedFences()
    {
        foreach (var fence in _stagedFences)
        {
            _ = close(fence);
        }

        _stagedFences.Clear();
    }

    private static bool CropsDccContent(OutputLayer layer, IBuffer buffer)
    {
        if (layer.SrcBox.IsEmpty || layer.SrcBox == new FBox(0, 0, buffer.Width, buffer.Height))
        {
            return false;
        }

        return buffer.TryGetDmabuf(out var attributes) &&
            attributes.Modifier >> 56 == 0x02 &&
            (attributes.Modifier & (1UL << 13)) != 0;
    }

    private bool ApplyLayers(
        IReadOnlyList<OutputLayer> layers, DrmAtomicBuilder builder,
        LiftoffOutput output, List<LiftoffLayer> pool, out bool anyAccepted)
    {
        anyAccepted = false;
        builder.Reset();
        while (pool.Count < layers.Count)
        {
            pool.Add(output.CreateLayer());
        }

        _layerFbIds.Clear();
        for (var i = 0; i < layers.Count; i++)
        {
            var layer = layers[i];
            var target = pool[i];
            var dst = layer.DstBox;
            uint fbId = 0;
            if (layer.Buffer is { } buffer && !dst.IsEmpty && !CropsDccContent(layer, buffer))
            {
                fbId = _backend.Framebuffers.GetOrAdd(buffer, layer.Opaque);
            }

            _layerFbIds.Add(fbId);
            if (fbId == 0)
            {
                target.Disable();
                continue;
            }

            target.SetFramebuffer(fbId);
            target.SetCrtcRect(dst.X, dst.Y, (uint)dst.Width, (uint)dst.Height);
            var src = layer.SrcBox.IsEmpty
                ? new FBox(0, 0, layer.Buffer!.Width, layer.Buffer.Height)
                : layer.SrcBox;
            target.SetProperty("SRC_X", LiftoffLayer.ToFixed16(src.X));
            target.SetProperty("SRC_Y", LiftoffLayer.ToFixed16(src.Y));
            target.SetProperty("SRC_W", LiftoffLayer.ToFixed16(src.Width));
            target.SetProperty("SRC_H", LiftoffLayer.ToFixed16(src.Height));
            target.SetZPos(i + 1);
            target.SetProperty(
                "IN_FENCE_FD",
                unchecked((ulong)(long)(layer.InFenceFd >= 0 ? StageFence(layer.InFenceFd) : -1)));

            target.SetAlpha(layer.Alpha >= 1f ? (ushort)0xFFFF : (ushort)Math.Clamp(layer.Alpha * 0xFFFF, 0, 0xFFFF));
        }

        for (var i = layers.Count; i < pool.Count; i++)
        {
            pool[i].Disable();
        }

        if (!output.TryApply(builder.Request, DrmAtomicCommitFlags.None, out var errno))
        {
            Log.Warn($"{Name}: liftoff apply failed (errno {errno}); all layers composited");
            return false;
        }

        var acceptedCount = 0;
        for (var i = 0; i < layers.Count; i++)
        {
            var accepted = _layerFbIds[i] != 0 && !pool[i].NeedsComposition;
            layers[i].Accepted = accepted;
            anyAccepted |= accepted;
            if (accepted)
            {
                acceptedCount++;
            }
        }

        Log.Debug($"{Name}: liftoff {(ReferenceEquals(builder, _testBuilder) ? "probe" : "commit")} placed {acceptedCount}/{layers.Count} layers");
        return true;
    }

    private bool DisableActiveLayers(DrmAtomicBuilder builder, bool allowModeset)
    {
        if (_liftoffOutput is null || !_liftoffActive)
        {
            return false;
        }

        foreach (var layer in _liftoffLayers)
        {
            layer.Disable();
        }

        var flags = allowModeset ? DrmAtomicCommitFlags.AllowModeset : DrmAtomicCommitFlags.None;
        if (!_liftoffOutput.TryApply(builder.Request, flags, out var errno))
        {
            Log.Warn($"{Name}: liftoff layer disable failed (errno {errno})");
        }

        _liftoffActive = false;
        return true;
    }

    private void ReleaseLayerLocks()
    {
        foreach (var held in _layerScanout)
        {
            held.Dispose();
        }

        _layerScanout.Clear();
        foreach (var held in _pendingLayerScanout)
        {
            held.Dispose();
        }

        _pendingLayerScanout.Clear();
        _layerFlipPending = false;
    }

    private static void RejectLayers(IReadOnlyList<OutputLayer> layers)
    {
        for (var i = 0; i < layers.Count; i++)
        {
            layers[i].Accepted = false;
        }
    }

    protected override void OnDestroy()
    {
        if (_backend.SessionActive && _hardwareLit)
        {
            DisablePipeline();
        }

        _liftoffOutput?.Dispose();
        _liftoffOutput = null;
        _liftoffLayers.Clear();
        ReleaseLayerLocks();
        _testBuilder.Dispose();
        _cursorRetry?.Remove();
        _cursorRetry = null;
        DropQueuedFrame();
        _queuedFlush?.Remove();
        _queuedFlush = null;
        _queuedFrame?.Dispose();
        _queuedFrame = null;
        _builder.Dispose();
        _scanout.Dispose();
        _pendingScanout.Dispose();
        _cursorBuffer.Dispose();
        _cursorScanout.Dispose();
        _pendingCursorScanout.Dispose();
        _backend.Framebuffers.ReleaseOrphans();
        if (_modeBlobId != 0)
        {
            Libdrm.drmModeDestroyPropertyBlob(_backend.Device.Fd, _modeBlobId);
            _modeBlobId = 0;
        }

        DestroyColorBlobs();
    }

    private void ReplaceModeBlob(DrmModeInfo mode)
    {
        if (_modeBlobId != 0)
        {
            Libdrm.drmModeDestroyPropertyBlob(_backend.Device.Fd, _modeBlobId);
        }

        var native = mode.Native;
        uint blobId;
        if (Libdrm.drmModeCreatePropertyBlob(_backend.Device.Fd, &native, (nuint)sizeof(_drmModeModeInfo), &blobId) != 0)
        {
            throw new InvalidOperationException("drmModeCreatePropertyBlob failed");
        }

        _modeBlobId = blobId;
    }

    private DrmModeInfo? FindNativeMode(OutputMode wanted)
    {
        DrmModeInfo? best = null;
        var bestDelta = long.MaxValue;
        foreach (var mode in _nativeModes)
        {
            if (mode.HorizontalDisplay != wanted.Width || mode.VerticalDisplay != wanted.Height)
            {
                continue;
            }

            var delta = Math.Abs((long)ToOutputMode(mode).RefreshMilliHz - wanted.RefreshMilliHz);
            if (delta < bestDelta)
            {
                bestDelta = delta;
                best = mode;
            }
        }

        foreach (var mode in _customModes)
        {
            if (mode.HorizontalDisplay != wanted.Width || mode.VerticalDisplay != wanted.Height)
            {
                continue;
            }

            var delta = Math.Abs((long)ToOutputMode(mode).RefreshMilliHz - wanted.RefreshMilliHz);
            if (delta < bestDelta)
            {
                bestDelta = delta;
                best = mode;
            }
        }

        return best;
    }

    private readonly List<DrmModeInfo> _customModes = [];

    [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Constructor)]
    private static extern DrmModeInfo WrapMode(ref _drmModeModeInfo native);

    private static bool TryBuildCustomMode(OutputMode wanted, out DrmModeInfo built)
    {
        built = default;
        if (!Libxcvt.TryGenerate(wanted.Width, wanted.Height, wanted.RefreshMilliHz, reducedBlanking: true, out var cvt))
        {
            return false;
        }

        var native = default(_drmModeModeInfo);
        native.clock = (uint)cvt.DotClock;
        native.hdisplay = (ushort)cvt.HDisplay;
        native.hsync_start = cvt.HSyncStart;
        native.hsync_end = cvt.HSyncEnd;
        native.htotal = cvt.HTotal;
        native.vdisplay = (ushort)cvt.VDisplay;
        native.vsync_start = cvt.VSyncStart;
        native.vsync_end = cvt.VSyncEnd;
        native.vtotal = cvt.VTotal;
        native.vrefresh = (uint)Math.Round(wanted.RefreshMilliHz / 1000.0);
        native.flags = (uint)(cvt.ModeFlags & 0xF);
        native.type = 1u << 5;
        built = WrapMode(ref native);
        return true;
    }

    private void RebuildCustomModes(IReadOnlyList<OutputMode> wanted)
    {
        _customModes.Clear();
        foreach (var mode in wanted)
        {
            if (TryBuildCustomMode(mode, out var built))
            {
                _customModes.Add(built);
            }
        }
    }

    private bool CustomModesGenerate(IReadOnlyList<OutputMode> wanted)
    {
        foreach (var mode in wanted)
        {
            if (!Libxcvt.TryGenerate(mode.Width, mode.Height, mode.RefreshMilliHz, reducedBlanking: true, out _))
            {
                return false;
            }
        }

        return true;
    }

    private bool PendingCustomModeMatches(OutputState state, OutputMode wanted)
    {
        if ((state.Fields & OutputStateFields.CustomModes) == 0 || state.CustomModes is not { } customs)
        {
            return false;
        }

        foreach (var mode in customs)
        {
            if (mode.Width == wanted.Width && mode.Height == wanted.Height &&
                Math.Abs(mode.RefreshMilliHz - wanted.RefreshMilliHz) < 500)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ModeEquals(OutputMode wanted, DrmModeInfo native) =>
        native.HorizontalDisplay == wanted.Width &&
        native.VerticalDisplay == wanted.Height &&
        Math.Abs(ToOutputMode(native).RefreshMilliHz - wanted.RefreshMilliHz) < 500;

    private static OutputMode ToOutputMode(DrmModeInfo mode)
    {
        var refresh = mode.HorizontalTotal > 0 && mode.VerticalTotal > 0
            ? (int)(mode.Clock * 1_000_000L / (mode.HorizontalTotal * mode.VerticalTotal))
            : (int)(mode.VerticalRefresh * 1000);
        return new OutputMode(mode.HorizontalDisplay, mode.VerticalDisplay, refresh);
    }

    private static uint RefreshIntervalNs(DrmModeInfo mode) => ToOutputMode(mode).RefreshIntervalNanoseconds;

    private byte[] ReadEdidBytes() => ReadEdidBytes(_backend.Device, _connectorProps);

    internal static EdidInfo ReadEdid(DrmDevice device, DrmPropertyMap connectorProps)
    {
        var bytes = ReadEdidBytes(device, connectorProps);
        return bytes.Length > 0 ? EdidInfo.Parse(bytes) : new EdidInfo("unknown", "unknown", string.Empty);
    }

    internal static byte[] ReadEdidBytes(DrmDevice device, DrmPropertyMap connectorProps)
    {
        if (connectorProps.TryGetValue("EDID", out var blobId) && blobId != 0)
        {
            try
            {
                var blob = device.GetPropertyBlob((uint)blobId);
                return blob.Data.Span.ToArray();
            }
            catch (DrmException)
            {
            }
        }

        return [];
    }

    internal static DrmFormatSet ReadInFormats(DrmDevice device, DrmPropertyMap planeProps)
    {
        var formats = new DrmFormatSet();
        if (!planeProps.TryGetValue("IN_FORMATS", out var blobId) || blobId == 0)
        {
            return formats;
        }

        var blob = device.GetPropertyBlob((uint)blobId);
        var data = blob.Data.Span;

        var countFormats = BitConverter.ToUInt32(data[8..]);
        var formatsOffset = BitConverter.ToUInt32(data[12..]);
        var countModifiers = BitConverter.ToUInt32(data[16..]);
        var modifiersOffset = BitConverter.ToUInt32(data[20..]);

        for (var m = 0; m < countModifiers; m++)
        {
            var at = (int)(modifiersOffset + m * 24);
            var mask = BitConverter.ToUInt64(data[at..]);
            var offset = BitConverter.ToUInt32(data[(at + 8)..]);
            var modifier = BitConverter.ToUInt64(data[(at + 16)..]);
            for (var bit = 0; bit < 64; bit++)
            {
                if ((mask & (1ul << bit)) == 0)
                {
                    continue;
                }

                var formatIndex = offset + bit;
                if (formatIndex < countFormats)
                {
                    var fourcc = BitConverter.ToUInt32(data[(int)(formatsOffset + formatIndex * 4)..]);
                    formats.Add((DrmFormat)fourcc, modifier);
                }
            }
        }

        return formats;
    }
}
