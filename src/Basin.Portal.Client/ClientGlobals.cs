using Basin.Protocol;
using Basin.Portal.Client.Protocol;
using Wayland;

namespace Basin.Portal.Client;

/// <summary>
/// Holds the Wayland globals that a portal client binds from the registry of the compositor.
/// Each property is <see langword="null"/> until <see cref="Bind"/> sees the matching global.
/// </summary>
public sealed class ClientGlobals : IDisposable
{
    /// <summary>
    /// Gets or sets the bound <c>wl_compositor</c>, at version 6 or lower.
    /// </summary>
    public WlCompositor? Compositor { get; set; }

    /// <summary>
    /// Gets or sets the bound <c>wl_shm</c>. Screen capture needs it for its shared-memory buffers.
    /// </summary>
    public WlShm? Shm { get; set; }

    /// <summary>
    /// Gets or sets the first <c>wl_seat</c> that the registry announced. A later seat is ignored.
    /// </summary>
    public WlSeat? Seat { get; set; }

    /// <summary>
    /// Gets the capabilities that the seat announced last. It is none until the first capabilities event arrives.
    /// </summary>
    public WlSeat.Capability SeatCapabilities { get; private set; }

    /// <summary>
    /// Occurs each time the seat sends a capabilities event. The argument is the new capability set.
    /// </summary>
    public event Action<WlSeat.Capability>? SeatCapabilitiesChanged;

    /// <summary>
    /// Gets or sets the bound <c>zxdg_output_manager_v1</c>, at version 3 or lower. It supplies the logical position and size of each output.
    /// </summary>
    public ZxdgOutputManagerV1? XdgOutputs { get; set; }

    /// <summary>
    /// Gets or sets the bound <c>ext_foreign_toplevel_list_v1</c>.
    /// </summary>
    public ExtForeignToplevelListV1? ToplevelList { get; set; }

    /// <summary>
    /// Gets or sets the bound <c>ext_image_copy_capture_manager_v1</c>. Screen capture is off without it.
    /// </summary>
    public ExtImageCopyCaptureManagerV1? Capture { get; set; }

    /// <summary>
    /// Gets or sets the bound <c>ext_output_image_capture_source_manager_v1</c>. Output and region capture need it.
    /// </summary>
    public ExtOutputImageCaptureSourceManagerV1? OutputSources { get; set; }

    /// <summary>
    /// Gets or sets the bound <c>ext_foreign_toplevel_image_capture_source_manager_v1</c>. Window capture needs it.
    /// </summary>
    public ExtForeignToplevelImageCaptureSourceManagerV1? ToplevelSources { get; set; }

    /// <summary>
    /// Gets or sets the bound <c>ext_data_control_manager_v1</c>. The clipboard portal needs it.
    /// </summary>
    public ExtDataControlManagerV1? DataControl { get; set; }

    /// <summary>
    /// Gets or sets the bound <c>zwp_virtual_keyboard_manager_v1</c>.
    /// </summary>
    public ZwpVirtualKeyboardManagerV1? VirtualKeyboards { get; set; }

    /// <summary>
    /// Gets or sets the bound <c>zwlr_virtual_pointer_manager_v1</c>, at version 2 or lower.
    /// </summary>
    public ZwlrVirtualPointerManagerV1? VirtualPointers { get; set; }

    /// <summary>
    /// Gets or sets the bound <c>hyprland_input_capture_manager_v1</c>. The input capture portal needs it.
    /// </summary>
    public HyprlandInputCaptureManagerV1? InputCapture { get; set; }

    /// <summary>
    /// Gets or sets the bound <c>hyprland_global_shortcuts_manager_v1</c>. The global shortcuts portal needs it.
    /// </summary>
    public HyprlandGlobalShortcutsManagerV1? GlobalShortcuts { get; set; }

    /// <summary>
    /// Gets or sets the bound <c>zwp_linux_dmabuf_v1</c>, at version 4 or lower. Capture into dmabuf targets needs it.
    /// </summary>
    public ZwpLinuxDmabufV1? Dmabuf { get; set; }

    /// <summary>
    /// Binds one registry global when its interface is one that this class knows. Other interfaces are ignored.
    /// The call caps the bound version at the highest version that the client implements.
    /// </summary>
    /// <param name="registry">The registry that announced the global.</param>
    /// <param name="name">The numeric name of the global in the registry.</param>
    /// <param name="iface">The interface name of the global, for example <c>wl_shm</c>.</param>
    /// <param name="version">The version that the compositor offers.</param>
    public void Bind(WlRegistry registry, uint name, string iface, uint version)
    {
        switch (iface)
        {
            case "wl_compositor":
                Compositor = registry.Bind<WlCompositor>(name, Math.Min(version, 6));
                break;
            case "wl_shm":
                Shm = registry.Bind<WlShm>(name, 1);
                break;
            case "wl_seat":
                if (Seat is null)
                {
                    var seat = registry.Bind<WlSeat>(name, Math.Min(version, 7));
                    Seat = seat;
                    seat.Capabilities += (_, e) =>
                    {
                        SeatCapabilities = e.Capabilities;
                        SeatCapabilitiesChanged?.Invoke(e.Capabilities);
                    };
                }

                break;
            case "zxdg_output_manager_v1":
                XdgOutputs = registry.Bind<ZxdgOutputManagerV1>(name, Math.Min(version, 3));
                break;
            case "ext_foreign_toplevel_list_v1":
                ToplevelList = registry.Bind<ExtForeignToplevelListV1>(name, 1);
                break;
            case "ext_image_copy_capture_manager_v1":
                Capture = registry.Bind<ExtImageCopyCaptureManagerV1>(name, 1);
                break;
            case "ext_output_image_capture_source_manager_v1":
                OutputSources = registry.Bind<ExtOutputImageCaptureSourceManagerV1>(name, 1);
                break;
            case "ext_foreign_toplevel_image_capture_source_manager_v1":
                ToplevelSources = registry.Bind<ExtForeignToplevelImageCaptureSourceManagerV1>(name, 1);
                break;
            case "ext_data_control_manager_v1":
                DataControl = registry.Bind<ExtDataControlManagerV1>(name, 1);
                break;
            case "zwp_virtual_keyboard_manager_v1":
                VirtualKeyboards = registry.Bind<ZwpVirtualKeyboardManagerV1>(name, 1);
                break;
            case "zwlr_virtual_pointer_manager_v1":
                VirtualPointers = registry.Bind<ZwlrVirtualPointerManagerV1>(name, Math.Min(version, 2));
                break;
            case "hyprland_input_capture_manager_v1":
                InputCapture = registry.Bind<HyprlandInputCaptureManagerV1>(name, 1);
                break;
            case "hyprland_global_shortcuts_manager_v1":
                GlobalShortcuts = registry.Bind<HyprlandGlobalShortcutsManagerV1>(name, 1);
                break;
            case "zwp_linux_dmabuf_v1":
                Dmabuf = registry.Bind<ZwpLinuxDmabufV1>(name, Math.Min(version, 4));
                break;
        }
    }

    /// <summary>
    /// Destroys every bound global that is not destroyed yet, in the reverse order of the properties.
    /// </summary>
    public void Dispose()
    {
        Destroy(Dmabuf);
        Destroy(GlobalShortcuts);
        Destroy(InputCapture);
        Destroy(VirtualPointers);
        Destroy(VirtualKeyboards);
        Destroy(DataControl);
        Destroy(ToplevelSources);
        Destroy(OutputSources);
        Destroy(Capture);
        Destroy(ToplevelList);
        Destroy(XdgOutputs);
        Destroy(Seat);
        Destroy(Shm);
        Destroy(Compositor);
    }

    private static void Destroy(WlProxy? proxy)
    {
        if (proxy is { IsDestroyed: false })
        {
            proxy.Dispose();
        }
    }
}
