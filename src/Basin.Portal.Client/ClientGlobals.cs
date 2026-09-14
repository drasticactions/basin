using Basin.Protocol;
using Basin.Portal.Client.Protocol;
using Wayland;

namespace Basin.Portal.Client;

public sealed class ClientGlobals : IDisposable
{
    public WlCompositor? Compositor { get; set; }

    public WlShm? Shm { get; set; }

    public WlSeat? Seat { get; set; }

    public WlSeat.Capability SeatCapabilities { get; private set; }

    public event Action<WlSeat.Capability>? SeatCapabilitiesChanged;

    public ZxdgOutputManagerV1? XdgOutputs { get; set; }

    public ExtForeignToplevelListV1? ToplevelList { get; set; }

    public ExtImageCopyCaptureManagerV1? Capture { get; set; }

    public ExtOutputImageCaptureSourceManagerV1? OutputSources { get; set; }

    public ExtForeignToplevelImageCaptureSourceManagerV1? ToplevelSources { get; set; }

    public ExtDataControlManagerV1? DataControl { get; set; }

    public ZwpVirtualKeyboardManagerV1? VirtualKeyboards { get; set; }

    public ZwlrVirtualPointerManagerV1? VirtualPointers { get; set; }

    public HyprlandInputCaptureManagerV1? InputCapture { get; set; }

    public HyprlandGlobalShortcutsManagerV1? GlobalShortcuts { get; set; }

    public ZwpLinuxDmabufV1? Dmabuf { get; set; }

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
