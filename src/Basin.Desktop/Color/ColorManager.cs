using System.Diagnostics.CodeAnalysis;
using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>wp_color_manager_v1</c> global. It tracks the image description of each surface and of each output, and tells clients the supported features.
/// </summary>
public sealed class ColorManager : IDisposable, IWpColorManagerV1Handler
{
    /// <summary>
    /// The protocol version that the global advertises.
    /// </summary>
    public const int Version = 2;

    private const uint ManagerErrorUnsupportedFeature = 0;
    private const uint ManagerErrorSurfaceExists = 1;
    private const uint CreatorErrorIncompleteSet = 0;
    private const uint CreatorErrorAlreadySet = 1;
    private const uint CreatorErrorInvalidTf = 3;
    private const uint CreatorErrorInvalidPrimariesNamed = 4;
    private const uint SurfaceErrorInert = 2;

    private static readonly ColorTransferFunction[] ConvertibleTransferFunctions =
    [
        ColorTransferFunction.CompoundPower24, ColorTransferFunction.Srgb, ColorTransferFunction.Gamma22,
        ColorTransferFunction.ExtLinear,
    ];

    private static readonly ColorTransferFunction[] DeprecatedTransferFunctions =
    [
        ColorTransferFunction.Srgb, ColorTransferFunction.ExtSrgb,
    ];

    private static readonly ColorPrimaries[] DeprecatedPrimaries = [];

    private IReadOnlyList<ColorTransferFunction>? _declaredTransferFunctions;
    private IReadOnlyList<ColorPrimaries>? _declaredPrimaries;
    private IReadOnlyList<ColorTransferFunction>? _derivedTransferFunctions;
    private IReadOnlyList<ColorPrimaries>? _derivedPrimaries;

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly Dictionary<Surface, SurfaceColor> _surfaces = [];
    private readonly HashSet<Surface> _claimed = [];
    private readonly Dictionary<OutputGlobal, ImageDescription> _outputs = [];
    private readonly List<(WpColorManagementOutputV1Resource Resource, OutputGlobal Output)> _outputResources = [];
    private readonly List<(WpColorManagementSurfaceFeedbackV1Resource Resource, Surface Surface)> _feedback = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="ColorManager"/> class and creates the global.
    /// </summary>
    /// <param name="display">The display that holds the global.</param>
    /// <param name="compositor">The compositor global whose surfaces get color state.</param>
    public ColorManager(WlServerDisplay display, CompositorGlobal compositor)
    {
        _compositor = compositor;
        _global = display.CreateGlobal(WpColorManagerV1.Interface, Version, OnBind);
    }

    /// <summary>
    /// Occurs when a surface commit applies a description with different content. The description is <see langword="null"/> when the client removed it.
    /// </summary>
    public event Action<Surface, ImageDescription?>? SurfaceDescriptionChanged;

    /// <summary>
    /// Gets the profile service. Clients can create ICC-based descriptions only when it reports <see cref="ColorFeatures.IccProfiles"/>.
    /// </summary>
    public IColorProfileService? Profiles { get; init; }

    /// <summary>
    /// Gets the resolver that converts surface content. It sets <see cref="CanConvert"/>.
    /// </summary>
    public IColorTransformResolver? Resolver { get; init; }

    /// <summary>
    /// Gets or sets the transfer functions that the global advertises. Unless set, the list is the convertible set, or gamma 2.2 only, plus each output's named function.
    /// </summary>
    public IReadOnlyList<ColorTransferFunction> SupportedTransferFunctions
    {
        get => _declaredTransferFunctions ?? (_derivedTransferFunctions ??= DeriveTransferFunctions());
        set => _declaredTransferFunctions = value;
    }

    /// <summary>
    /// Gets or sets the primaries that the global advertises. Unless set, the list is sRGB plus the named primaries of each output.
    /// </summary>
    public IReadOnlyList<ColorPrimaries> SupportedPrimaries
    {
        get => _declaredPrimaries ?? (_derivedPrimaries ??= DerivePrimaries());
        set => _declaredPrimaries = value;
    }

    /// <summary>
    /// Gets a value indicating whether the <see cref="Resolver"/> can convert content between descriptions.
    /// </summary>
    public bool CanConvert => Resolver is { Capability: not ColorTransformCapability.None };

    private IReadOnlyList<ColorTransferFunction> DeriveTransferFunctions()
    {
        var transfers = new List<ColorTransferFunction>();
        if (CanConvert)
        {
            transfers.AddRange(ConvertibleTransferFunctions);
        }
        else
        {
            transfers.Add(ColorTransferFunction.Gamma22);
        }

        foreach (var description in _outputs.Values)
        {
            if (description.TransferNamed is { } transfer && !transfers.Contains(transfer))
            {
                transfers.Add(transfer);
            }
        }

        return transfers;
    }

    private IReadOnlyList<ColorPrimaries> DerivePrimaries()
    {
        var primaries = new List<ColorPrimaries> { ColorPrimaries.Srgb };
        foreach (var description in _outputs.Values)
        {
            if (description.PrimariesNamed is { } named && !primaries.Contains(named))
            {
                primaries.Add(named);
            }
        }

        return primaries;
    }

    /// <summary>
    /// Removes the global. Bound clients keep their resources.
    /// </summary>
    public void Dispose() => _global.Dispose();

    /// <summary>
    /// Gets the description that the surface's last applied commit set.
    /// </summary>
    /// <param name="surface">The surface to look up.</param>
    /// <returns>The current description, or <see cref="ImageDescription.SdrDefault"/> when the surface has none.</returns>
    public ImageDescription DescriptionOf(Surface surface) =>
        _surfaces.TryGetValue(surface, out var entry) && entry.Current is { } description
            ? description
            : ImageDescription.SdrDefault;

    private sealed class SurfaceColor
    {
        public ImageDescription? Current;
        public WpColorManagerV1.RenderIntent CurrentIntent;
        public bool HasPending;
        public ImageDescription? Pending;
        public WpColorManagerV1.RenderIntent PendingIntent;
        public bool HasLatched;
        public ImageDescription? Latched;
        public WpColorManagerV1.RenderIntent LatchedIntent;
    }

    private SurfaceColor StateFor(Surface surface)
    {
        if (_surfaces.TryGetValue(surface, out var state))
        {
            return state;
        }

        state = new SurfaceColor();
        _surfaces[surface] = state;

        surface.CommitRequested += () => Latch(state);
        surface.Committed += () => ApplyLatched(surface, state);
        surface.Destroyed += () =>
        {
            _claimed.Remove(surface);
            _surfaces.Remove(surface);
        };
        return state;
    }

    private static void Latch(SurfaceColor state)
    {
        if (!state.HasPending)
        {
            return;
        }

        state.HasPending = false;
        state.HasLatched = true;
        state.Latched = state.Pending;
        state.LatchedIntent = state.PendingIntent;
    }

    private void ApplyLatched(Surface surface, SurfaceColor state)
    {
        if (!state.HasLatched)
        {
            return;
        }

        state.HasLatched = false;
        state.CurrentIntent = state.LatchedIntent;
        var next = state.Latched;
        var changed = !ImageDescription.ContentComparer.Equals(state.Current, next);
        state.Current = next;
        if (changed)
        {
            SurfaceDescriptionChanged?.Invoke(surface, next);
        }
    }

    /// <summary>
    /// Occurs when <see cref="SetOutputDescription"/> sets a description whose content differs from the previous one.
    /// </summary>
    public event Action<OutputGlobal, ImageDescription>? OutputDescriptionChanged;

    /// <summary>
    /// Occurs when <see cref="RemoveOutputDescription"/> removes a known output.
    /// </summary>
    public event Action<OutputGlobal>? OutputDescriptionRemoved;

    /// <summary>
    /// Gets the current description of every output that has one.
    /// </summary>
    public IEnumerable<ImageDescription> OutputDescriptions => _outputs.Values;

    /// <summary>
    /// Finds the description of an output.
    /// </summary>
    /// <param name="output">The output's global.</param>
    /// <param name="description">The description, or <see langword="null"/> when the output has none.</param>
    /// <returns><see langword="true"/> when the output has a description.</returns>
    public bool TryGetOutputDescription(OutputGlobal output, [NotNullWhen(true)] out ImageDescription? description) =>
        _outputs.TryGetValue(output, out description);

    /// <summary>
    /// Removes the description of an output and resets the derived supported lists. It does nothing for an unknown output.
    /// </summary>
    /// <param name="output">The output's global.</param>
    public void RemoveOutputDescription(OutputGlobal output)
    {
        if (_outputs.Remove(output))
        {
            _derivedTransferFunctions = null;
            _derivedPrimaries = null;
            OutputDescriptionRemoved?.Invoke(output);
        }
    }

    /// <summary>
    /// Sets the description of an output. It notifies the bound output and feedback resources each time, and raises <see cref="OutputDescriptionChanged"/> only on a content change.
    /// </summary>
    /// <param name="output">The output's global.</param>
    /// <param name="description">The new description.</param>
    public void SetOutputDescription(OutputGlobal output, ImageDescription description)
    {
        var changed = !_outputs.TryGetValue(output, out var previous) ||
            !ImageDescription.ContentComparer.Equals(previous, description);
        _outputs[output] = description;
        if (changed)
        {
            _derivedTransferFunctions = null;
            _derivedPrimaries = null;
        }

        foreach (var (resource, resourceOutput) in _outputResources)
        {
            if (resourceOutput == output && !resource.IsDestroyed)
            {
                resource.SendImageDescriptionChanged();
            }
        }

        foreach (var (resource, _) in _feedback)
        {
            if (!resource.IsDestroyed)
            {
                SendPreferred(resource, description);
            }
        }

        if (changed)
        {
            OutputDescriptionChanged?.Invoke(output, description);
        }
    }

    private static void SendPreferred(WpColorManagementSurfaceFeedbackV1Resource resource, ImageDescription description)
    {
        if (resource.SupportsSendPreferredChanged2)
        {
            resource.SendPreferredChanged2((uint)(description.Identity >> 32), (uint)description.Identity);
            return;
        }

#pragma warning disable CS0618
        resource.SendPreferredChanged((uint)description.Identity);
#pragma warning restore CS0618
    }

    private static void SendReady(WpImageDescriptionV1Resource resource, ImageDescription description)
    {
        if (resource.SupportsSendReady2)
        {
            resource.SendReady2((uint)(description.Identity >> 32), (uint)description.Identity);
            return;
        }

#pragma warning disable CS0618
        resource.SendReady((uint)description.Identity);
#pragma warning restore CS0618
    }

    private ImageDescription PreferredFor(Surface surface)
    {
        foreach (var output in surface.EnteredOutputs)
        {
            if (_outputs.TryGetValue(output, out var description))
            {
                return description;
            }
        }

        return _outputs.Count > 0 ? _outputs.Values.First() : ImageDescription.SdrDefault;
    }

    private bool OffersScrgb => SupportedTransferFunctions.Contains(ColorTransferFunction.ExtLinear);

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new WpColorManagerV1Resource(client, version, id, this);
        manager.SendSupportedIntent(WpColorManagerV1.RenderIntent.Perceptual);
        if (manager.Version >= 2)
        {
            manager.SendSupportedIntent(WpColorManagerV1.RenderIntent.AbsoluteNoAdaptation);
        }

        if (Profiles?.Features.HasFlag(ColorFeatures.IccProfiles) == true)
        {
            manager.SendSupportedFeature(WpColorManagerV1.Feature.IccV2V4);
        }

        manager.SendSupportedFeature(WpColorManagerV1.Feature.Parametric);
        manager.SendSupportedFeature(WpColorManagerV1.Feature.SetPrimaries);
        manager.SendSupportedFeature(WpColorManagerV1.Feature.SetTfPower);
        manager.SendSupportedFeature(WpColorManagerV1.Feature.SetLuminances);
        manager.SendSupportedFeature(WpColorManagerV1.Feature.SetMasteringDisplayPrimaries);
        if (OffersScrgb)
        {
            manager.SendSupportedFeature(WpColorManagerV1.Feature.WindowsScrgb);
        }

        foreach (var tf in SupportedTransferFunctions)
        {
            if (tf == ColorTransferFunction.CompoundPower24 && manager.Version < 2)
            {
                continue;
            }

            if (IsDeprecatedAt(tf, manager.Version))
            {
                continue;
            }

            manager.SendSupportedTfNamed((WpColorManagerV1.TransferFunction)tf);
        }

        foreach (var primaries in SupportedPrimaries)
        {
            if (IsDeprecatedAt(primaries, manager.Version))
            {
                continue;
            }

            manager.SendSupportedPrimariesNamed((WpColorManagerV1.Primaries)primaries);
        }

        manager.SendDone();
    }

    void IWpColorManagerV1Handler.Destroy(WpColorManagerV1Resource resource)
    {
    }

    void IWpColorManagerV1Handler.CreateIccCreator(WpColorManagerV1Resource resource, in WpColorManagerV1Resource.CreateIccCreatorEventArgs args)
    {
        if (Profiles is { } profiles && profiles.Features.HasFlag(ColorFeatures.IccProfiles))
        {
            _ = new IccCreator(resource.Client, resource.Version, args.Obj, profiles);
        }
        else
        {
            resource.PostError(ManagerErrorUnsupportedFeature, "ICC image descriptions are not supported.");
        }
    }

    void IWpColorManagerV1Handler.CreateWindowsScrgb(WpColorManagerV1Resource resource, in WpColorManagerV1Resource.CreateWindowsScrgbEventArgs args)
    {
        if (!OffersScrgb)
        {
            resource.PostError(ManagerErrorUnsupportedFeature, "windows_scrgb is not supported.");
            return;
        }

        DescribeInto(new DescriptionObject(resource.Client, resource.Version, args.ImageDescription), ImageDescription.WindowsScrgb(), allowInformation: false);
    }

    void IWpColorManagerV1Handler.CreateWindowsBt2100(WpColorManagerV1Resource resource, in WpColorManagerV1Resource.CreateWindowsBt2100EventArgs args) =>
        resource.PostError(ManagerErrorUnsupportedFeature, "windows_bt2100 is not supported.");

    void IWpColorManagerV1Handler.CreateParametricCreator(WpColorManagerV1Resource resource, in WpColorManagerV1Resource.CreateParametricCreatorEventArgs args) =>
        _ = new ParametricCreator(resource.Client, resource.Version, args.Obj, this);

    void IWpColorManagerV1Handler.GetImageDescription(WpColorManagerV1Resource resource, in WpColorManagerV1Resource.GetImageDescriptionEventArgs args)
    {
        var target = new DescriptionObject(resource.Client, resource.Version, args.ImageDescription);
        if (ReferenceRegistry.TryResolve(args.Reference, out var referenced, out var allowInformation))
        {
            DescribeInto(target, referenced, allowInformation);
            return;
        }

        target.Resource.SendFailed(WpImageDescriptionV1.Cause.NoOutput, "unknown image description reference");
    }

    void IWpColorManagerV1Handler.GetOutput(WpColorManagerV1Resource resource, in WpColorManagerV1Resource.GetOutputEventArgs args)
    {
        var handler = new OutputColor(this, resource.Client, resource.Version, args.Id);
        var output = OutputGlobal.FromResource(args.Output);
        if (output is null)
        {
            return;
        }

        handler.Attach(output);
    }

    void IWpColorManagerV1Handler.GetSurface(WpColorManagerV1Resource resource, in WpColorManagerV1Resource.GetSurfaceEventArgs args)
    {
        var handler = new SurfaceColorObject(this, resource.Client, resource.Version, args.Id);
        var surface = _compositor.ResolveSurface(args.Surface);
        if (surface is null)
        {
            return;
        }

        if (!_claimed.Add(surface))
        {
            resource.PostError(ManagerErrorSurfaceExists, "The surface already has a color management object.");
            return;
        }

        handler.Attach(surface, StateFor(surface));
    }

    void IWpColorManagerV1Handler.GetSurfaceFeedback(WpColorManagerV1Resource resource, in WpColorManagerV1Resource.GetSurfaceFeedbackEventArgs args)
    {
        var handler = new FeedbackObject(this, resource.Client, resource.Version, args.Id);
        var surface = _compositor.ResolveSurface(args.Surface);
        if (surface is null)
        {
            return;
        }

        handler.Attach(surface);
    }

    /// <summary>
    /// Tests whether a protocol version deprecates a transfer function. From version 2, sRGB and extended sRGB are deprecated.
    /// </summary>
    /// <param name="transfer">The transfer function to test.</param>
    /// <param name="version">The bound protocol version.</param>
    /// <returns><see langword="true"/> when the client must not get the value at that version.</returns>
    public static bool IsDeprecatedAt(ColorTransferFunction transfer, uint version) =>
        version >= 2 && Array.IndexOf(DeprecatedTransferFunctions, transfer) >= 0;

    /// <summary>
    /// Tests whether a protocol version deprecates a set of primaries. No primaries are deprecated now.
    /// </summary>
    /// <param name="primaries">The primaries to test.</param>
    /// <param name="version">The bound protocol version.</param>
    /// <returns><see langword="true"/> when the client must not get the value at that version.</returns>
    public static bool IsDeprecatedAt(ColorPrimaries primaries, uint version) =>
        version >= 2 && Array.IndexOf(DeprecatedPrimaries, primaries) >= 0;

    /// <summary>
    /// Creates an image description reference resource that other color requests can resolve to <paramref name="description"/>.
    /// </summary>
    /// <param name="client">The client that owns the new resource.</param>
    /// <param name="version">The resource version.</param>
    /// <param name="id">The new object id.</param>
    /// <param name="description">The description that the reference names.</param>
    /// <param name="allowInformation">Whether a description created from the reference can answer <c>get_information</c>.</param>
    /// <returns>The new resource. It stays registered until the client destroys it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="description"/> is <see langword="null"/>.</exception>
    public static WpImageDescriptionReferenceV1Resource CreateReference(
        WlClient client,
        uint version,
        uint id,
        ImageDescription description,
        bool allowInformation)
    {
        ArgumentNullException.ThrowIfNull(description);
        var resource = new WpImageDescriptionReferenceV1Resource(client, version, id);
        ReferenceRegistry.Register(resource, description, allowInformation);
        return resource;
    }

    private static void DescribeInto(
        DescriptionObject target,
        ImageDescription description,
        bool allowInformation = true)
    {
        DescriptionRegistry.Register(target.Resource, description);
        if (!allowInformation)
        {
            target.NoInformation = "This image description has no information.";
            SendReady(target.Resource, description);
            return;
        }

        target.Information = description;
        SendReady(target.Resource, description);
    }

    private static void SendInformation(WpImageDescriptionV1Resource resource, uint id, ImageDescription description)
    {
        var info = new WpImageDescriptionInfoV1Resource(resource.Client, resource.Version, id);
        if (description.IccData is { } icc)
        {
            using var blob = Wayland.Server.Shm.ShmBlobs
                .ForClient(resource.Client)
                .Create("basin-icc-profile", icc);
            info.SendIccFile(blob.FdSlot, blob.Size);
        }

        if (description.PrimariesNamed is { } primariesNamed)
        {
            info.SendPrimariesNamed((WpColorManagerV1.Primaries)primariesNamed);
        }

        if (description.PrimariesCustom is { } p)
        {
            info.SendPrimaries(p.Rx, p.Ry, p.Gx, p.Gy, p.Bx, p.By, p.Wx, p.Wy);
        }

        if (description.TransferNamed is { } tf)
        {
            info.SendTfNamed((WpColorManagerV1.TransferFunction)tf);
        }

        if (description.TransferPower is { } power)
        {
            info.SendTfPower(power);
        }

        if (description.Luminances is { } luminances)
        {
            info.SendLuminances(luminances.Min, luminances.Max, luminances.Reference);
        }

        if (description.MasteringPrimaries is { } mp)
        {
            info.SendTargetPrimaries(mp.Rx, mp.Ry, mp.Gx, mp.Gy, mp.Bx, mp.By, mp.Wx, mp.Wy);
        }

        if (description.MasteringLuminance is { } ml)
        {
            info.SendTargetLuminance(ml.Min, ml.Max);
        }

        if (description.MaxCll is { } cll)
        {
            info.SendTargetMaxCll(cll);
        }

        if (description.MaxFall is { } fall)
        {
            info.SendTargetMaxFall(fall);
        }

        info.SendDone();
    }

    private sealed class DescriptionObject : IWpImageDescriptionV1Handler
    {
        public DescriptionObject(WlClient client, uint version, uint id)
        {
            Resource = new WpImageDescriptionV1Resource(client, version, id, this);
        }

        public WpImageDescriptionV1Resource Resource { get; }

        public ImageDescription? Information { get; set; }

        public string? NoInformation { get; set; }

        void IWpImageDescriptionV1Handler.Destroy(WpImageDescriptionV1Resource resource)
        {
        }

        void IWpImageDescriptionV1Handler.GetInformation(WpImageDescriptionV1Resource resource, in WpImageDescriptionV1Resource.GetInformationEventArgs args)
        {
            if (NoInformation is { } message)
            {
                resource.PostError((uint)WpImageDescriptionV1.Error.NoInformation, message);
                return;
            }

            if (Information is { } description)
            {
                SendInformation(resource, args.Information, description);
            }
        }
    }

    private sealed class OutputColor : IWpColorManagementOutputV1Handler
    {
        private readonly ColorManager _manager;
        private OutputGlobal? _output;

        public OutputColor(ColorManager manager, WlClient client, uint version, uint id)
        {
            _manager = manager;
            Resource = new WpColorManagementOutputV1Resource(client, version, id, this);
        }

        public WpColorManagementOutputV1Resource Resource { get; }

        public void Attach(OutputGlobal output)
        {
            _output = output;
            _manager._outputResources.Add((Resource, output));
            Resource.Destroyed += OnDestroyed;
        }

        void IWpColorManagementOutputV1Handler.Destroy(WpColorManagementOutputV1Resource resource)
        {
        }

        void IWpColorManagementOutputV1Handler.GetImageDescription(
            WpColorManagementOutputV1Resource resource,
            in WpColorManagementOutputV1Resource.GetImageDescriptionEventArgs args)
        {
            if (_output is not { } output)
            {
                return;
            }

            var description = _manager._outputs.GetValueOrDefault(output, ImageDescription.SdrDefault);
            DescribeInto(new DescriptionObject(resource.Client, resource.Version, args.ImageDescription), description);
        }

        private void OnDestroyed(object? sender, EventArgs e) => _manager._outputResources.Remove((Resource, _output!));
    }

    private sealed class SurfaceColorObject : IWpColorManagementSurfaceV1Handler
    {
        private readonly ColorManager _manager;
        private Surface? _surface;
        private SurfaceColor? _state;
        private bool _inert;

        public SurfaceColorObject(ColorManager manager, WlClient client, uint version, uint id)
        {
            _manager = manager;
            Resource = new WpColorManagementSurfaceV1Resource(client, version, id, this);
        }

        public WpColorManagementSurfaceV1Resource Resource { get; }

        public void Attach(Surface surface, SurfaceColor state)
        {
            _surface = surface;
            _state = state;
            surface.Destroyed += OnSurfaceDestroyed;
            Resource.Destroyed += OnResourceDestroyed;
        }

        void IWpColorManagementSurfaceV1Handler.Destroy(WpColorManagementSurfaceV1Resource resource)
        {
        }

        void IWpColorManagementSurfaceV1Handler.SetImageDescription(
            WpColorManagementSurfaceV1Resource resource,
            in WpColorManagementSurfaceV1Resource.SetImageDescriptionEventArgs args)
        {
            if (_state is not { } state)
            {
                return;
            }

            if (_inert)
            {
                resource.PostError(SurfaceErrorInert, "The surface of this object is destroyed.");
                return;
            }

            var description = DescriptionRegistry.Resolve(args.ImageDescription?.RawHandle ?? 0);
            if (description is null)
            {
                return;
            }

            state.HasPending = true;
            state.Pending = description;
            state.PendingIntent = args.RenderIntent;
        }

        void IWpColorManagementSurfaceV1Handler.UnsetImageDescription(WpColorManagementSurfaceV1Resource resource)
        {
            if (_state is not { } state)
            {
                return;
            }

            if (_inert)
            {
                resource.PostError(SurfaceErrorInert, "The surface of this object is destroyed.");
                return;
            }

            state.HasPending = true;
            state.Pending = null;
            state.PendingIntent = default;
        }

        private void OnSurfaceDestroyed() => _inert = true;

        private void OnResourceDestroyed(object? sender, EventArgs e)
        {
            _manager._claimed.Remove(_surface!);
            if (_inert)
            {
                return;
            }

            var state = _state!;
            state.HasPending = true;
            state.Pending = null;
            state.PendingIntent = default;
        }
    }

    private sealed class FeedbackObject : IWpColorManagementSurfaceFeedbackV1Handler
    {
        private readonly ColorManager _manager;
        private Surface? _surface;

        public FeedbackObject(ColorManager manager, WlClient client, uint version, uint id)
        {
            _manager = manager;
            Resource = new WpColorManagementSurfaceFeedbackV1Resource(client, version, id, this);
        }

        public WpColorManagementSurfaceFeedbackV1Resource Resource { get; }

        public void Attach(Surface surface)
        {
            _surface = surface;
            _manager._feedback.Add((Resource, surface));
            Resource.Destroyed += OnDestroyed;
        }

        void IWpColorManagementSurfaceFeedbackV1Handler.Destroy(WpColorManagementSurfaceFeedbackV1Resource resource)
        {
        }

        void IWpColorManagementSurfaceFeedbackV1Handler.GetPreferred(
            WpColorManagementSurfaceFeedbackV1Resource resource,
            in WpColorManagementSurfaceFeedbackV1Resource.GetPreferredEventArgs args)
        {
            if (_surface is not { } surface)
            {
                return;
            }

            DescribeInto(new DescriptionObject(resource.Client, resource.Version, args.ImageDescription), _manager.PreferredFor(surface));
        }

        void IWpColorManagementSurfaceFeedbackV1Handler.GetPreferredParametric(
            WpColorManagementSurfaceFeedbackV1Resource resource,
            in WpColorManagementSurfaceFeedbackV1Resource.GetPreferredParametricEventArgs args)
        {
            if (_surface is not { } surface)
            {
                return;
            }

            DescribeInto(new DescriptionObject(resource.Client, resource.Version, args.ImageDescription), _manager.PreferredFor(surface));
        }

        private void OnDestroyed(object? sender, EventArgs e) => _manager._feedback.Remove((Resource, _surface!));
    }

    internal static class ReferenceRegistry
    {
        private static readonly Dictionary<nint, (ImageDescription Description, bool AllowInformation)> Entries = [];

        public static void Register(
            WpImageDescriptionReferenceV1Resource resource,
            ImageDescription description,
            bool allowInformation)
        {
            var raw = resource.RawHandle;
            Entries[raw] = (description, allowInformation);
            resource.Destroyed += (_, _) => Entries.Remove(raw);
        }

        public static bool TryResolve(
            WpImageDescriptionReferenceV1Resource? resource,
            out ImageDescription description,
            out bool allowInformation)
        {
            if (resource is { IsDestroyed: false } && Entries.TryGetValue(resource.RawHandle, out var entry))
            {
                (description, allowInformation) = entry;
                return true;
            }

            description = ImageDescription.SdrDefault;
            allowInformation = false;
            return false;
        }
    }

    internal static class DescriptionRegistry
    {
        private static readonly Dictionary<nint, ImageDescription> Descriptions = [];

        public static void Register(WpImageDescriptionV1Resource resource, ImageDescription description)
        {
            var raw = resource.RawHandle;
            Descriptions[raw] = description;
            resource.Destroyed += (_, _) => Descriptions.Remove(raw);
        }

        public static ImageDescription? Resolve(nint handle) => Descriptions.GetValueOrDefault(handle);
    }

    private sealed class IccCreator : IWpImageDescriptionCreatorIccV1Handler
    {
        private const uint ErrorAlreadySet = 1;
        private const uint ErrorBadFd = 2;
        private const uint ErrorBadSize = 3;
        private const uint ErrorOutOfFile = 4;
        private const uint MaxIccSize = 32 * 1024 * 1024;

        private readonly WpImageDescriptionCreatorIccV1Resource _resource;
        private readonly IColorProfileService _profiles;
        private byte[]? _data;
        private bool _readFailed;

        public IccCreator(WlClient client, uint version, uint id, IColorProfileService profiles)
        {
            _profiles = profiles;
            _resource = new WpImageDescriptionCreatorIccV1Resource(client, version, id, this);
        }

        void IWpImageDescriptionCreatorIccV1Handler.SetIccFile(
            WpImageDescriptionCreatorIccV1Resource resource,
            in WpImageDescriptionCreatorIccV1Resource.SetIccFileEventArgs args) =>
            OnSetIccFile(args.IccProfile, args.Offset, args.Length);

        void IWpImageDescriptionCreatorIccV1Handler.Create(
            WpImageDescriptionCreatorIccV1Resource resource,
            in WpImageDescriptionCreatorIccV1Resource.CreateEventArgs args) =>
            OnCreate(new DescriptionObject(resource.Client, resource.Version, args.ImageDescription));

        private void OnSetIccFile(int fd, uint offset, uint length)
        {
            using var handle = new Microsoft.Win32.SafeHandles.SafeFileHandle(fd, ownsHandle: true);
            if (_data is not null || _readFailed)
            {
                _resource.PostError(ErrorAlreadySet, "The ICC file is already set.");
                return;
            }

            if (length == 0 || length > MaxIccSize)
            {
                _resource.PostError(ErrorBadSize, "The ICC length must be more than 0 and not more than 32 MB.");
                return;
            }

            long fileSize;
            try
            {
                fileSize = System.IO.RandomAccess.GetLength(handle);
            }
            catch (IOException)
            {
                _resource.PostError(ErrorBadFd, "The ICC fd is not seekable.");
                return;
            }

            if (offset + (long)length > fileSize)
            {
                _resource.PostError(ErrorOutOfFile, "The offset plus the length is more than the file size.");
                return;
            }

            var data = new byte[length];
            try
            {
                var read = 0;
                while (read < data.Length)
                {
                    var got = System.IO.RandomAccess.Read(handle, data.AsSpan(read), offset + read);
                    if (got == 0)
                    {
                        break;
                    }

                    read += got;
                }

                if (read != data.Length)
                {
                    _readFailed = true;
                    return;
                }
            }
            catch (IOException)
            {
                _readFailed = true;
                return;
            }

            _data = data;
        }

        private void OnCreate(DescriptionObject target)
        {
            if (_readFailed)
            {
                target.Resource.SendFailed(WpImageDescriptionV1.Cause.OperatingSystem, "reading the ICC file failed");
                return;
            }

            if (_data is not { } data)
            {
                _resource.PostError(CreatorErrorIncompleteSet, "The image description needs an ICC file.");
                return;
            }

            if (!_profiles.TryParseIcc(data, out var parsed))
            {
                target.Resource.SendFailed(WpImageDescriptionV1.Cause.Unsupported, "unusable ICC profile");
                return;
            }

            var description = parsed with { IccData = data };
            DescriptionRegistry.Register(target.Resource, description);
            target.NoInformation = "An image description made from an ICC file has no information.";

            SendReady(target.Resource, description);
        }
    }

    private sealed class ParametricCreator : IWpImageDescriptionCreatorParamsV1Handler
    {
        private readonly WpImageDescriptionCreatorParamsV1Resource _resource;
        private readonly ColorManager _manager;
        private ColorPrimaries? _primariesNamed;
        private (int, int, int, int, int, int, int, int)? _primariesCustom;
        private ColorTransferFunction? _tfNamed;
        private uint? _tfPower;
        private (uint, uint, uint)? _luminances;
        private (int, int, int, int, int, int, int, int)? _masteringPrimaries;
        private (uint, uint)? _masteringLuminance;
        private uint? _maxCll;
        private uint? _maxFall;

        public ParametricCreator(WlClient client, uint version, uint id, ColorManager manager)
        {
            _manager = manager;
            _resource = new WpImageDescriptionCreatorParamsV1Resource(client, version, id, this);
        }

        void IWpImageDescriptionCreatorParamsV1Handler.SetPrimariesNamed(
            WpImageDescriptionCreatorParamsV1Resource resource,
            in WpImageDescriptionCreatorParamsV1Resource.SetPrimariesNamedEventArgs args)
        {
            if (!_manager.SupportedPrimaries.Contains((ColorPrimaries)args.Primaries) ||
                IsDeprecatedAt((ColorPrimaries)args.Primaries, resource.Version))
            {
                resource.PostError(CreatorErrorInvalidPrimariesNamed, "These primaries are not supported.");
                return;
            }

            Set(ref _primariesNamed, (ColorPrimaries)args.Primaries);
        }

        void IWpImageDescriptionCreatorParamsV1Handler.SetPrimaries(
            WpImageDescriptionCreatorParamsV1Resource resource,
            in WpImageDescriptionCreatorParamsV1Resource.SetPrimariesEventArgs args) =>
            Set(ref _primariesCustom, (args.RX, args.RY, args.GX, args.GY, args.BX, args.BY, args.WX, args.WY));

        void IWpImageDescriptionCreatorParamsV1Handler.SetTfNamed(
            WpImageDescriptionCreatorParamsV1Resource resource,
            in WpImageDescriptionCreatorParamsV1Resource.SetTfNamedEventArgs args)
        {
            if (!_manager.SupportedTransferFunctions.Contains((ColorTransferFunction)args.Tf) ||
                IsDeprecatedAt((ColorTransferFunction)args.Tf, resource.Version))
            {
                resource.PostError(CreatorErrorInvalidTf, "This transfer function is not supported.");
                return;
            }

            Set(ref _tfNamed, (ColorTransferFunction)args.Tf);
        }

        void IWpImageDescriptionCreatorParamsV1Handler.SetTfPower(
            WpImageDescriptionCreatorParamsV1Resource resource,
            in WpImageDescriptionCreatorParamsV1Resource.SetTfPowerEventArgs args) =>
            Set(ref _tfPower, args.Eexp);

        void IWpImageDescriptionCreatorParamsV1Handler.SetLuminances(
            WpImageDescriptionCreatorParamsV1Resource resource,
            in WpImageDescriptionCreatorParamsV1Resource.SetLuminancesEventArgs args) =>
            Set(ref _luminances, (args.MinLum, args.MaxLum, args.ReferenceLum));

        void IWpImageDescriptionCreatorParamsV1Handler.SetMasteringDisplayPrimaries(
            WpImageDescriptionCreatorParamsV1Resource resource,
            in WpImageDescriptionCreatorParamsV1Resource.SetMasteringDisplayPrimariesEventArgs args) =>
            Set(ref _masteringPrimaries, (args.RX, args.RY, args.GX, args.GY, args.BX, args.BY, args.WX, args.WY));

        void IWpImageDescriptionCreatorParamsV1Handler.SetMasteringLuminance(
            WpImageDescriptionCreatorParamsV1Resource resource,
            in WpImageDescriptionCreatorParamsV1Resource.SetMasteringLuminanceEventArgs args) =>
            Set(ref _masteringLuminance, (args.MinLum, args.MaxLum));

        void IWpImageDescriptionCreatorParamsV1Handler.SetMaxCll(
            WpImageDescriptionCreatorParamsV1Resource resource,
            in WpImageDescriptionCreatorParamsV1Resource.SetMaxCllEventArgs args) =>
            Set(ref _maxCll, args.MaxCll);

        void IWpImageDescriptionCreatorParamsV1Handler.SetMaxFall(
            WpImageDescriptionCreatorParamsV1Resource resource,
            in WpImageDescriptionCreatorParamsV1Resource.SetMaxFallEventArgs args) =>
            Set(ref _maxFall, args.MaxFall);

        void IWpImageDescriptionCreatorParamsV1Handler.Create(
            WpImageDescriptionCreatorParamsV1Resource resource,
            in WpImageDescriptionCreatorParamsV1Resource.CreateEventArgs args)
        {
            var target = new DescriptionObject(resource.Client, resource.Version, args.ImageDescription);
            if ((_primariesNamed is null && _primariesCustom is null) || (_tfNamed is null && _tfPower is null))
            {
                resource.PostError(CreatorErrorIncompleteSet, "The image description needs primaries and a transfer function.");
                return;
            }

            var description = new ImageDescription
            {
                PrimariesNamed = _primariesNamed,
                PrimariesCustom = _primariesCustom,
                TransferNamed = _tfNamed,
                TransferPower = _tfPower,
                Luminances = _luminances,
                MasteringPrimaries = _masteringPrimaries,
                MasteringLuminance = _masteringLuminance,
                MaxCll = _maxCll,
                MaxFall = _maxFall,
            };
            DescribeInto(target, description);
        }

        private void Set<T>(ref T? slot, T value)
            where T : struct
        {
            if (slot is not null)
            {
                _resource.PostError(CreatorErrorAlreadySet, "This property is already set.");
                return;
            }

            slot = value;
        }
    }
}
