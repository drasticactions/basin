dam(1)

# NAME

dam - a Wayland kiosk compositor

# SYNOPSIS

*dam* [options...] [--] [_application_...]

# DESCRIPTION

Dam runs a single, maximized application. Dam can run multiple applications,
but only a single one is visible at any point in time. User interaction and
activities outside the scope of the running application are prevented.

Dam is a drop-in replacement for *cage*(1), built on the basin compositor
libraries. Every cage option is accepted with its cage meaning. Dam exits
when the application's whole process tree has closed the pipe it inherits,
and returns the application's exit status.

# OPTIONS

*-d*, *--no-decorations*
	Don't draw client side decorations when possible.

*-D*
	Enable debug logging. Equivalent to *--log-level* debug.

*-h*, *--help*
	Show the help message.

*-m* <mode>, *--output-mode* <mode>
	Set the multi-monitor behavior. Supported modes are:
	*last* Dam uses only the last connected monitor.
	*extend* Dam extends the display across all connected monitors.

*-s*, *--allow-vt-switch*
	Allow VT switching.

*-v*, *--version*
	Show the version number and exit.

*-x*, *--no-xwayland*
	Disable Xwayland.

*--backend* <kind>
	Where the outputs go: *nested*, *drm* or *headless*. The default is
	nested inside a Wayland session and drm outside one.

*--renderer* <name>
	The renderer to draw with. *--help* lists the names. The default is
	vulkan, falling back through gl to pixman.

*--frames* <n>
	Render this many frames and exit.

*--log-level* <level>
	Discard diagnostics below this: trace, debug, info, warn or error.

*--alloc-report*
	Report what the run allocated on stdout at exit.

# ENVIRONMENT

_DISPLAY_
	Set in the application's environment to the name of the X display used
	for Xwayland, unless *-x* was given.

_WAYLAND_DISPLAY_
	Set in the application's environment to the name of the Wayland display
	that Dam is running on.

_XCURSOR_PATH_
	Directory where cursors are located.

_XCURSOR_SIZE_
	Specifies the configured cursor size.

_XCURSOR_THEME_
	Specifies the configured cursor theme.

_XKB_DEFAULT_RULES_, _XKB_DEFAULT_MODEL_, _XKB_DEFAULT_LAYOUT_,
_XKB_DEFAULT_VARIANT_, _XKB_DEFAULT_OPTIONS_
	Configures the xkb keyboard settings. See *xkeyboard-config*(7).

# SEE ALSO

*cage*(1), *xkeyboard-config*(7)
