using Basin.Backend.Wayland.Protocol;
using Basin.Capabilities;
using Basin.Protocol;
using Wayland;

namespace Basin.Backend.Wayland;

internal sealed class ParentSurfaceColor : IDisposable
{
    private readonly WaylandBackend _backend;
    private readonly WlSurface _surface;
    private readonly Dictionary<ImageDescription, Entry> _entries = new(ImageDescription.ContentComparer);
    private WpColorManagementSurfaceV1? _colorSurface;
    private ImageDescription? _applied;

    private sealed class Entry
    {
        public required WpImageDescriptionV1 Proxy;
        public bool Ready;
        public bool Failed;
    }

    public ParentSurfaceColor(WaylandBackend backend, WlSurface surface)
    {
        _backend = backend;
        _surface = surface;
    }

    public ImageDescription? Applied => _applied;

    public bool Stage(ImageDescription? description)
    {
        if (_backend.ParentColorManager is not { } manager)
        {
            return false;
        }

        if (description is null || ImageDescription.ContentComparer.Equals(description, ImageDescription.SdrDefault))
        {
            if (_applied is not null)
            {
                _colorSurface?.UnsetImageDescription();
                _applied = null;
            }

            return true;
        }

        if (_applied is not null && ImageDescription.ContentComparer.Equals(_applied, description))
        {
            return true;
        }

        if (!_entries.TryGetValue(description, out var entry))
        {
            if (Create(manager, description) is not { } created)
            {
                return false;
            }

            entry = created;
            _entries[description] = entry;
        }

        if (entry.Failed || !entry.Ready)
        {
            return false;
        }

        _colorSurface ??= manager.GetSurface(_surface);
        _colorSurface.SetImageDescription(entry.Proxy, WpColorManagerV1.RenderIntent.Perceptual);
        _applied = description;
        return true;
    }

    private Entry? Create(WpColorManagerV1 manager, ImageDescription description)
    {
        var features = _backend.ParentColorFeatures;
        if (description.IccData is not null || !features.Contains(WpColorManagerV1.Feature.Parametric))
        {
            return null;
        }

        var transfer = description.TransferNamed is { } named &&
            _backend.ParentColorTransfers.Contains((WpColorManagerV1.TransferFunction)named);
        var power = !transfer && description.TransferPower is not null && features.Contains(WpColorManagerV1.Feature.SetTfPower);
        if (!transfer && !power)
        {
            return null;
        }

        var primaries = description.PrimariesNamed ?? (description.PrimariesCustom is null ? ColorPrimaries.Srgb : null);
        var namedPrimaries = primaries is { } p && _backend.ParentColorPrimaries.Contains((WpColorManagerV1.Primaries)p);
        var customPrimaries = !namedPrimaries && description.PrimariesCustom is not null &&
            features.Contains(WpColorManagerV1.Feature.SetPrimaries);
        if (!namedPrimaries && !customPrimaries)
        {
            return null;
        }

        var creator = manager.CreateParametricCreator();
        if (transfer)
        {
            creator.SetTfNamed((WpColorManagerV1.TransferFunction)description.TransferNamed!.Value);
        }
        else
        {
            creator.SetTfPower(description.TransferPower!.Value);
        }

        if (namedPrimaries)
        {
            creator.SetPrimariesNamed((WpColorManagerV1.Primaries)primaries!.Value);
        }
        else
        {
            var c = description.PrimariesCustom!.Value;
            creator.SetPrimaries(c.Rx, c.Ry, c.Gx, c.Gy, c.Bx, c.By, c.Wx, c.Wy);
        }

        if (description.Luminances is { } luminances && features.Contains(WpColorManagerV1.Feature.SetLuminances))
        {
            creator.SetLuminances(luminances.Min, luminances.Max, luminances.Reference);
        }

        if (features.Contains(WpColorManagerV1.Feature.SetMasteringDisplayPrimaries))
        {
            if (description.MasteringPrimaries is { } m)
            {
                creator.SetMasteringDisplayPrimaries(m.Rx, m.Ry, m.Gx, m.Gy, m.Bx, m.By, m.Wx, m.Wy);
            }

            if (description.MasteringLuminance is { } ml)
            {
                creator.SetMasteringLuminance(ml.Min, ml.Max);
            }
        }

        if (description.MaxCll is { } cll)
        {
            creator.SetMaxCll(cll);
        }

        if (description.MaxFall is { } fall)
        {
            creator.SetMaxFall(fall);
        }

        var entry = new Entry { Proxy = creator.Create() };
#pragma warning disable CS0618
        entry.Proxy.Ready += (_, _) => entry.Ready = true;
#pragma warning restore CS0618
        entry.Proxy.Ready2 += (_, _) => entry.Ready = true;
        entry.Proxy.Failed += (_, e) =>
        {
            entry.Failed = true;
            Log.Debug($"parent: image description for passthrough refused, {e.Msg}");
        };
        return entry;
    }

    public void Dispose()
    {
        _colorSurface?.Dispose();
        _colorSurface = null;
        foreach (var entry in _entries.Values)
        {
            entry.Proxy.Dispose();
        }

        _entries.Clear();
    }

    private static Basin.Diagnostics.BasinLogger Log => WaylandBackendLog.Log;
}
