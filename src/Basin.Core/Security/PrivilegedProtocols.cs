namespace Basin;

public static class PrivilegedProtocols
{
    private static readonly HashSet<string> Names =
    [
        "zwlr_screencopy_manager_v1",
        "ext_image_copy_capture_manager_v1",
        "ext_output_image_capture_source_manager_v1",
        "ext_foreign_toplevel_image_capture_source_manager_v1",
        "zwlr_export_dmabuf_manager_v1",
        "zkde_screencast_unstable_v1",

        "zwlr_data_control_manager_v1",
        "ext_data_control_manager_v1",

        "zwlr_output_manager_v1",
        "kde_output_device_registry_v2",
        "kde_output_management_v2",
        "kde_external_brightness_v1",
        "kde_output_order_v1",
        "org_kde_kwin_dpms_manager",
        "zwlr_output_power_manager_v1",
        "zwlr_gamma_control_manager_v1",
        "wp_drm_lease_device_v1",

        "zwlr_foreign_toplevel_manager_v1",
        "ext_foreign_toplevel_list_v1",
        "ext_workspace_manager_v1",
        "org_kde_plasma_virtual_desktop_management",
        "org_kde_plasma_window_management",
        "org_kde_plasma_shell",
        "kde_screen_edge_manager_v1",
        "kde_lockscreen_overlay_v1",

        "org_kde_kwin_fake_input",
        "zwp_virtual_keyboard_manager_v1",
        "zwlr_virtual_pointer_manager_v1",
        "ext_transient_seat_manager_v1",
        "zwp_input_method_manager_v2",

        "zwp_xwayland_keyboard_grab_manager_v1",

        "ext_session_lock_manager_v1",
        "wp_security_context_manager_v1",

        "zwlr_layer_shell_v1",
    ];

    public static bool Contains(string interfaceName) => Names.Contains(interfaceName);

    public static IReadOnlyCollection<string> All => Names;
}
