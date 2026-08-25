using Basin.Desktop;
using Basin.Diagnostics;
using Wayland.Server;
using Xunit;

namespace Basin.Tests;

public sealed class KioskPackTests
{
    private static readonly string[] CageGlobals =
    [
        "wl_shm",
        "wl_compositor",
        "wl_subcompositor",
        "wp_viewporter",
        "wp_presentation",
        "wl_seat",
        "wl_data_device_manager",
        "xdg_wm_base",
        "zxdg_decoration_manager_v1",
        "org_kde_kwin_server_decoration_manager",
        "zwp_primary_selection_device_manager_v1",
        "zxdg_output_manager_v1",
        "zwlr_output_manager_v1",
        "zwlr_gamma_control_manager_v1",
        "wp_drm_lease_device_v1",
        "zwlr_foreign_toplevel_manager_v1",
        "zwlr_screencopy_manager_v1",
        "zwlr_export_dmabuf_manager_v1",
        "wp_single_pixel_buffer_manager_v1",
        "zwp_relative_pointer_manager_v1",
        "zwp_virtual_keyboard_manager_v1",
        "zwlr_virtual_pointer_manager_v1",
        "wp_cursor_shape_manager_v1",
        "ext_idle_notifier_v1",
    ];

    private static readonly string[] ColorGlobals =
    [
        "wp_color_manager_v1",
        "wp_color_representation_manager_v1",
        "wp_alpha_modifier_v1",
    ];

    private static string[] Expected => [.. CageGlobals, .. ColorGlobals];

    [Fact]
    public void KioskPack_advertises_cages_globals_and_colour()
    {
        var wireInterfaces = KioskPack.Default.Select(m => m.WireInterface).Order().ToArray();
        Assert.Equal(Expected.Order().ToArray(), wireInterfaces);
    }

    [Fact]
    public void KioskPack_carries_no_desktop_escape_hatch()
    {
        string[] forbidden =
        [
            "ext_session_lock_manager_v1",
            "zwlr_layer_shell_v1",
            "zwlr_data_control_manager_v1",
            "ext_data_control_manager_v1",
            "ext_workspace_manager_v1",
            "wp_security_context_manager_v1",
            "xdg_activation_v1",
            "zwp_text_input_manager_v3",
            "zwp_pointer_constraints_v1",
            "wp_fractional_scale_manager_v1",
            "xdg_wm_dialog_v1",
            "xdg_toplevel_icon_manager_v1",
            "xdg_toplevel_tag_manager_v1",
            "xdg_toplevel_drag_manager_v1",
            "xwayland_shell_v1",
        ];

        var pack = KioskPack.Default;
        foreach (var wireInterface in forbidden)
        {
            Assert.False(pack.Contains(wireInterface), $"{wireInterface} leaked into the kiosk pack");
        }
    }

    [Fact]
    public void KioskPack_installs_and_freezes_with_a_layout_alone()
    {
        CompositorTestHost.SkipWithoutWaylandClient();
        BasinCounters.Reset();
        using var display = WlServerDisplay.Create();
        var loop = new WaylandEventLoop(display);
        using (var services = new BasinServices(display, loop).Use(new OutputLayout()))
        {
            services.Install(KioskPack.Default);
            services.Freeze();
            Assert.Equal(Expected.Order().ToArray(), services.Modules.Keys.Order().ToArray());
        }

        Assert.SkipWhen(!BasinCounters.Enabled, "lifetime tracking is compiled out in this configuration");
        Assert.Equal(0, BasinCounters.LiveObjects);
    }
}
