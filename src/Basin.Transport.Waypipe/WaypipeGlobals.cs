using System.Buffers.Binary;

namespace Basin.Transport.Waypipe;

/// <summary>
/// Decides which globals a remote client on one channel can see, and which buffer formats its dmabuf global offers.
/// </summary>
public sealed class WaypipeGlobals
{
    /// <summary>
    /// The device number that the substitute dmabuf global announces when the host has no dmabuf of its own.
    /// </summary>
    public const ulong SyntheticMainDevice = 0x626173696e;

    /// <summary>
    /// Gets a new set of the linear formats that a channel without video can carry as dmabuf.
    /// </summary>
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

    /// <summary>
    /// Gets a new set of the linear formats that a channel with video can carry. It holds only opaque formats, because a decoded frame has no alpha.
    /// </summary>
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

    /// <summary>
    /// Gets a new set of the formats for this channel: <see cref="VideoChannelFormats"/> with video, otherwise <see cref="ChannelFormats"/>.
    /// </summary>
    public DrmFormatSet Formats => _carriesVideo ? VideoChannelFormats : ChannelFormats;

    private static readonly Dictionary<string, string> Refused = new(StringComparer.Ordinal)
    {
        ["wp_linux_drm_syncobj_manager_v1"] = "the channel carries no explicit-sync timelines",
        ["zwp_linux_explicit_synchronization_v1"] = "the channel carries no explicit-sync timelines",
        ["wp_drm_lease_device_v1"] = "there is no local device for a remote client to lease",
    };

    private readonly bool _carriesDmabuf;
    private readonly bool _carriesVideo;

    /// <summary>
    /// Initializes a new instance of the <see cref="WaypipeGlobals"/> class for a channel without video.
    /// </summary>
    /// <param name="carriesDmabuf">A value indicating whether the channel shows the dmabuf global.</param>
    public WaypipeGlobals(bool carriesDmabuf)
        : this(carriesDmabuf, carriesVideo: false)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WaypipeGlobals"/> class.
    /// </summary>
    /// <param name="carriesDmabuf">A value indicating whether the channel shows the dmabuf global.</param>
    /// <param name="carriesVideo">A value indicating whether the channel decodes video, which limits the formats.</param>
    public WaypipeGlobals(bool carriesDmabuf, bool carriesVideo)
    {
        _carriesDmabuf = carriesDmabuf;
        _carriesVideo = carriesVideo;
    }

    /// <summary>
    /// Tells whether a remote client on this channel can see a global.
    /// </summary>
    /// <param name="interfaceName">The Wayland interface name of the global.</param>
    /// <returns><see langword="true"/> when <see cref="WhyWithheld"/> gives <see langword="null"/>.</returns>
    public bool Carries(string interfaceName) => WhyWithheld(interfaceName) is null;

    /// <summary>
    /// Gives the reason that a global is hidden from a remote client. The dmabuf global, the explicit-sync globals and DRM lease are the candidates.
    /// </summary>
    /// <param name="interfaceName">The Wayland interface name of the global.</param>
    /// <returns>The reason as text, or <see langword="null"/> when the client can see the global.</returns>
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
