using System.Buffers.Binary;

namespace Basin.Transport.Waypipe;

public sealed class WaypipeGlobals
{
    public const ulong SyntheticMainDevice = 0x626173696e;

    public static DrmFormatSet ChannelFormats
    {
        get
        {
            var formats = new DrmFormatSet();
            formats.Add(DrmFormat.Argb8888, DrmFormatSet.ModifierLinear);
            formats.Add(DrmFormat.Xrgb8888, DrmFormatSet.ModifierLinear);
            formats.Add(DrmFormat.Abgr8888, DrmFormatSet.ModifierLinear);
            formats.Add(DrmFormat.Xbgr8888, DrmFormatSet.ModifierLinear);
            formats.Add(DrmFormat.Xrgb2101010, DrmFormatSet.ModifierLinear);
            formats.Add(DrmFormat.Argb2101010, DrmFormatSet.ModifierLinear);
            formats.Add(DrmFormat.Xbgr2101010, DrmFormatSet.ModifierLinear);
            formats.Add(DrmFormat.Abgr2101010, DrmFormatSet.ModifierLinear);
            return formats;
        }
    }

    public static DrmFormatSet VideoChannelFormats
    {
        get
        {
            var formats = new DrmFormatSet();
            formats.Add(DrmFormat.Xrgb8888, DrmFormatSet.ModifierLinear);
            formats.Add(DrmFormat.Xbgr8888, DrmFormatSet.ModifierLinear);
            formats.Add(DrmFormat.Xrgb2101010, DrmFormatSet.ModifierLinear);
            formats.Add(DrmFormat.Xbgr2101010, DrmFormatSet.ModifierLinear);
            return formats;
        }
    }

    public DrmFormatSet Formats => _carriesVideo ? VideoChannelFormats : ChannelFormats;

    private static readonly Dictionary<string, string> Refused = new(StringComparer.Ordinal)
    {
        ["wp_linux_drm_syncobj_manager_v1"] = "the channel carries no explicit-sync timelines",
        ["zwp_linux_explicit_synchronization_v1"] = "the channel carries no explicit-sync timelines",
        ["wp_drm_lease_device_v1"] = "there is no local device for a remote client to lease",
    };

    private readonly bool _carriesDmabuf;
    private readonly bool _carriesVideo;

    public WaypipeGlobals(bool carriesDmabuf)
        : this(carriesDmabuf, carriesVideo: false)
    {
    }

    public WaypipeGlobals(bool carriesDmabuf, bool carriesVideo)
    {
        _carriesDmabuf = carriesDmabuf;
        _carriesVideo = carriesVideo;
    }

    public bool Carries(string interfaceName) => WhyWithheld(interfaceName) is null;

    public string? WhyWithheld(string interfaceName)
    {
        ArgumentNullException.ThrowIfNull(interfaceName);
        if (interfaceName == "zwp_linux_dmabuf_v1")
        {
            return _carriesDmabuf
                ? null
                : "the channel was not asked for dmabuf (--gpu), so a client must stay shm-only";
        }

        return Refused.GetValueOrDefault(interfaceName);
    }
}
