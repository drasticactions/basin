using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class ColorManager : IDisposable
{
    public const int Version = 2;

    private const uint ManagerErrorUnsupportedFeature = 0;
    private const uint ManagerErrorSurfaceExists = 1;
    private const uint CreatorErrorIncompleteSet = 0;
    private const uint CreatorErrorAlreadySet = 1;
    private const uint CreatorErrorInvalidTf = 3;
    private const uint CreatorErrorInvalidPrimariesNamed = 4;
    private const uint SurfaceErrorInert = 2;

    private static readonly ColorTransferFunction[] AllTransferFunctions =
    [
        ColorTransferFunction.Srgb, ColorTransferFunction.Gamma22, ColorTransferFunction.ExtLinear,
        ColorTransferFunction.St2084Pq, ColorTransferFunction.Hlg, ColorTransferFunction.CompoundPower24,
    ];

    private static readonly ColorPrimaries[] AllPrimaries =
    [
        ColorPrimaries.Srgb, ColorPrimaries.Bt2020, ColorPrimaries.DciP3, ColorPrimaries.DisplayP3,
    ];

    private readonly WlGlobal _global;
    private readonly CompositorGlobal _compositor;
    private readonly Dictionary<Surface, SurfaceColor> _surfaces = [];
    private readonly HashSet<Surface> _claimed = [];
    private readonly Dictionary<OutputGlobal, ImageDescription> _outputs = [];
    private readonly List<(WpColorManagementOutputV1Resource Resource, OutputGlobal Output)> _outputResources = [];
    private readonly List<(WpColorManagementSurfaceFeedbackV1Resource Resource, Surface Surface)> _feedback = [];

    public ColorManager(WlServerDisplay display, CompositorGlobal compositor)
    {
        _compositor = compositor;
        _global = display.CreateGlobal(WpColorManagerV1.Interface, Version, OnBind);
    }

    public event Action<Surface, ImageDescription?>? SurfaceDescriptionChanged;

    public IColorProfileService? Profiles { get; init; }

    public IReadOnlyList<ColorTransferFunction> SupportedTransferFunctions { get; set; } = AllTransferFunctions;

    public IReadOnlyList<ColorPrimaries> SupportedPrimaries { get; set; } = AllPrimaries;

    public void Dispose() => _global.Dispose();

    public ImageDescription DescriptionOf(Surface surface) =>
        _surfaces.TryGetValue(surface, out var entry) && entry.Current is { } description
            ? description
            : ImageDescription.Srgb;

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

    public event Action<OutputGlobal, ImageDescription>? OutputDescriptionChanged;

    public void SetOutputDescription(OutputGlobal output, ImageDescription description)
    {
        var changed = !_outputs.TryGetValue(output, out var previous) ||
            !ImageDescription.ContentComparer.Equals(previous, description);
        _outputs[output] = description;
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

        return _outputs.Count > 0 ? _outputs.Values.First() : ImageDescription.Srgb;
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new WpColorManagerV1Resource(client, version, id);
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
        foreach (var tf in SupportedTransferFunctions)
        {
            if (tf == ColorTransferFunction.CompoundPower24 && manager.Version < 2)
            {
                continue;
            }

            manager.SendSupportedTfNamed((WpColorManagerV1.TransferFunction)tf);
        }

        foreach (var primaries in SupportedPrimaries)
        {
            manager.SendSupportedPrimariesNamed((WpColorManagerV1.Primaries)primaries);
        }

        manager.SendDone();

        manager.CreateIccCreator += (_, e) =>
        {
            if (Profiles is { } profiles && profiles.Features.HasFlag(ColorFeatures.IccProfiles))
            {
                _ = new IccCreator(new WpImageDescriptionCreatorIccV1Resource(client, manager.Version, e.Obj), profiles);
            }
            else
            {
                manager.PostError(ManagerErrorUnsupportedFeature, "ICC image descriptions are not offered");
            }
        };
        manager.CreateWindowsScrgb += (_, e) =>
            manager.PostError(ManagerErrorUnsupportedFeature, "windows_scrgb is not offered");
        manager.CreateWindowsBt2100 += (_, e) =>
            manager.PostError(ManagerErrorUnsupportedFeature, "windows_bt2100 is not offered");

        manager.CreateParametricCreator += (_, e) =>
            _ = new ParametricCreator(new WpImageDescriptionCreatorParamsV1Resource(client, manager.Version, e.Obj), this);

        manager.GetImageDescription += (_, e) =>
        {
            var target = new WpImageDescriptionV1Resource(client, manager.Version, e.ImageDescription);
            if (ReferenceRegistry.TryResolve(e.Reference, out var referenced, out var allowInformation))
            {
                DescribeInto(target, referenced, allowInformation);
                return;
            }

            target.SendFailed(WpImageDescriptionV1.Cause.NoOutput, "unknown image description reference");
        };

        manager.GetOutput += (_, e) =>
        {
            var resource = new WpColorManagementOutputV1Resource(client, manager.Version, e.Id);
            var output = OutputGlobal.FromResource(e.Output);
            if (output is null)
            {
                return;
            }

            var entry = (resource, output);
            _outputResources.Add(entry);
            resource.Destroyed += (_, _) => _outputResources.Remove(entry);
            resource.GetImageDescription += (_, ge) =>
            {
                var description = _outputs.GetValueOrDefault(output, ImageDescription.Srgb);
                DescribeInto(new WpImageDescriptionV1Resource(client, manager.Version, ge.ImageDescription), description);
            };
        };

        manager.GetSurface += (_, e) =>
        {
            var resource = new WpColorManagementSurfaceV1Resource(client, manager.Version, e.Id);
            var surface = _compositor.ResolveSurface(e.Surface);
            if (surface is null)
            {
                return;
            }

            if (!_claimed.Add(surface))
            {
                manager.PostError(ManagerErrorSurfaceExists, "surface already has a color management object");
                return;
            }

            var state = StateFor(surface);
            var inert = false;
            surface.Destroyed += () => inert = true;

            resource.SetImageDescription += (_, se) =>
            {
                if (inert)
                {
                    resource.PostError(SurfaceErrorInert, "the surface this object was created for is gone");
                    return;
                }

                var description = DescriptionRegistry.Resolve(se.ImageDescription?.RawHandle ?? 0);
                if (description is null)
                {
                    return;
                }

                state.HasPending = true;
                state.Pending = description;
                state.PendingIntent = se.RenderIntent;
            };
            resource.UnsetImageDescription += (_, _) =>
            {
                if (inert)
                {
                    resource.PostError(SurfaceErrorInert, "the surface this object was created for is gone");
                    return;
                }

                state.HasPending = true;
                state.Pending = null;
                state.PendingIntent = default;
            };
            resource.Destroyed += (_, _) =>
            {
                _claimed.Remove(surface);
                if (inert)
                {
                    return;
                }

                state.HasPending = true;
                state.Pending = null;
                state.PendingIntent = default;
            };
        };

        manager.GetSurfaceFeedback += (_, e) =>
        {
            var resource = new WpColorManagementSurfaceFeedbackV1Resource(client, manager.Version, e.Id);
            var surface = _compositor.ResolveSurface(e.Surface);
            if (surface is null)
            {
                return;
            }

            var entry = (resource, surface);
            _feedback.Add(entry);
            resource.Destroyed += (_, _) => _feedback.Remove(entry);
            resource.GetPreferred += (_, ge) =>
                DescribeInto(new WpImageDescriptionV1Resource(client, manager.Version, ge.ImageDescription), PreferredFor(surface));
            resource.GetPreferredParametric += (_, ge) =>
                DescribeInto(new WpImageDescriptionV1Resource(client, manager.Version, ge.ImageDescription), PreferredFor(surface));
        };
    }

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
        WpImageDescriptionV1Resource resource,
        ImageDescription description,
        bool allowInformation = true)
    {
        DescriptionRegistry.Register(resource, description);
        if (!allowInformation)
        {
            resource.GetInformation += (_, _) => resource.PostError(
                (uint)WpImageDescriptionV1.Error.NoInformation,
                "this image description carries no information");
            SendReady(resource, description);
            return;
        }

        resource.GetInformation += (_, ie) =>
        {
            var info = new WpImageDescriptionInfoV1Resource(resource.Client, resource.Version, ie.Information);
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
        };

        SendReady(resource, description);
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

            description = ImageDescription.Srgb;
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

    private sealed class IccCreator
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

        public IccCreator(WpImageDescriptionCreatorIccV1Resource resource, IColorProfileService profiles)
        {
            _resource = resource;
            _profiles = profiles;
            resource.SetIccFile += (_, e) => OnSetIccFile(e.IccProfile, e.Offset, e.Length);
            resource.Create += (_, e) => OnCreate(new WpImageDescriptionV1Resource(resource.Client, resource.Version, e.ImageDescription));
        }

        private void OnSetIccFile(int fd, uint offset, uint length)
        {
            using var handle = new Microsoft.Win32.SafeHandles.SafeFileHandle(fd, ownsHandle: true);
            if (_data is not null || _readFailed)
            {
                _resource.PostError(ErrorAlreadySet, "ICC file already set");
                return;
            }

            if (length == 0 || length > MaxIccSize)
            {
                _resource.PostError(ErrorBadSize, "ICC length must be within (0, 32MB]");
                return;
            }

            long fileSize;
            try
            {
                fileSize = System.IO.RandomAccess.GetLength(handle);
            }
            catch (IOException)
            {
                _resource.PostError(ErrorBadFd, "ICC fd is not seekable");
                return;
            }

            if (offset + (long)length > fileSize)
            {
                _resource.PostError(ErrorOutOfFile, "offset + length exceeds the file");
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

        private void OnCreate(WpImageDescriptionV1Resource target)
        {
            if (_readFailed)
            {
                target.SendFailed(WpImageDescriptionV1.Cause.OperatingSystem, "reading the ICC file failed");
                return;
            }

            if (_data is not { } data)
            {
                _resource.PostError(CreatorErrorIncompleteSet, "an ICC file is required");
                return;
            }

            if (!_profiles.TryParseIcc(data, out var parsed))
            {
                target.SendFailed(WpImageDescriptionV1.Cause.Unsupported, "unusable ICC profile");
                return;
            }

            var description = parsed with { IccData = data };
            DescriptionRegistry.Register(target, description);
            target.GetInformation += (_, _) =>
                target.PostError((uint)WpImageDescriptionV1.Error.NoInformation, "ICC-created descriptions carry no information");

            SendReady(target, description);
        }
    }

    private sealed class ParametricCreator
    {
        private readonly WpImageDescriptionCreatorParamsV1Resource _resource;
        private ColorPrimaries? _primariesNamed;
        private (int, int, int, int, int, int, int, int)? _primariesCustom;
        private ColorTransferFunction? _tfNamed;
        private uint? _tfPower;
        private (uint, uint, uint)? _luminances;
        private (int, int, int, int, int, int, int, int)? _masteringPrimaries;
        private (uint, uint)? _masteringLuminance;
        private uint? _maxCll;
        private uint? _maxFall;

        public ParametricCreator(WpImageDescriptionCreatorParamsV1Resource resource, ColorManager manager)
        {
            _resource = resource;
            resource.SetPrimariesNamed += (_, e) =>
            {
                if (!manager.SupportedPrimaries.Contains((ColorPrimaries)e.Primaries))
                {
                    resource.PostError(CreatorErrorInvalidPrimariesNamed, "primaries are not offered");
                    return;
                }

                Set(ref _primariesNamed, (ColorPrimaries)e.Primaries);
            };
            resource.SetPrimaries += (_, e) => Set(ref _primariesCustom, (e.RX, e.RY, e.GX, e.GY, e.BX, e.BY, e.WX, e.WY));
            resource.SetTfNamed += (_, e) =>
            {
                if (!manager.SupportedTransferFunctions.Contains((ColorTransferFunction)e.Tf))
                {
                    resource.PostError(CreatorErrorInvalidTf, "transfer function is not offered");
                    return;
                }

                Set(ref _tfNamed, (ColorTransferFunction)e.Tf);
            };
            resource.SetTfPower += (_, e) => Set(ref _tfPower, e.Eexp);
            resource.SetLuminances += (_, e) => Set(ref _luminances, (e.MinLum, e.MaxLum, e.ReferenceLum));
            resource.SetMasteringDisplayPrimaries += (_, e) => Set(ref _masteringPrimaries, (e.RX, e.RY, e.GX, e.GY, e.BX, e.BY, e.WX, e.WY));
            resource.SetMasteringLuminance += (_, e) => Set(ref _masteringLuminance, (e.MinLum, e.MaxLum));
            resource.SetMaxCll += (_, e) => Set(ref _maxCll, e.MaxCll);
            resource.SetMaxFall += (_, e) => Set(ref _maxFall, e.MaxFall);
            resource.Create += (_, e) =>
            {
                var target = new WpImageDescriptionV1Resource(resource.Client, resource.Version, e.ImageDescription);
                if ((_primariesNamed is null && _primariesCustom is null) || (_tfNamed is null && _tfPower is null))
                {
                    resource.PostError(CreatorErrorIncompleteSet, "primaries and transfer function are required");
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
            };
        }

        private void Set<T>(ref T? slot, T value)
            where T : struct
        {
            if (slot is not null)
            {
                _resource.PostError(CreatorErrorAlreadySet, "property already set");
                return;
            }

            slot = value;
        }
    }
}
