namespace Basin.Ipc;

internal static class IpcSchemas
{
    private const string Empty = """{"type":"object","properties":{}}""";

    private const string Id = """
        {"type":"object","properties":{"id":{"anyOf":[{"type":"integer","minimum":0},{"type":"string","minLength":1}],"description":"The window id from windows/list, an app_id, or part of a title. An active window wins a tie. A string of decimal digits is an id, because a JSON number above 2^53 loses digits in JavaScript."}},"required":["id"]}
        """;

    private const string WorkspaceId = """
        {"type":"object","properties":{"id":{"anyOf":[{"type":"integer","minimum":0},{"type":"string","pattern":"^[0-9]+$"}],"description":"The workspace id from workspaces/list. A string of decimal digits is also accepted, because a JSON number above 2^53 loses digits in JavaScript."}},"required":["id"]}
        """;

    private const string OutputEntries = """
        {"type":"object","properties":{"entries":{"type":"array","description":"One entry per output that changes. An entry names only what it changes.","items":{"type":"object","properties":{
        "name":{"type":"string","description":"The output name from outputs/list."},
        "enabled":{"type":"boolean","description":"Whether the output is on. The default is its current state."},
        "mode":{"type":"object","properties":{"width":{"type":"integer"},"height":{"type":"integer"},"refresh_mhz":{"type":"integer","description":"The refresh rate in millihertz. The default is the current rate."}},"required":["width","height"]},
        "position":{"type":"object","properties":{"x":{"type":"integer"},"y":{"type":"integer"}},"required":["x","y"]},
        "scale":{"type":"number","exclusiveMinimum":0,"maximum":16},
        "transform":{"type":"string","enum":["normal","90","180","270","flipped","flipped-90","flipped-180","flipped-270"]},
        "adaptive_sync":{"type":"boolean"}},"required":["name"]}}},"required":["entries"]}
        """;

    private const string CaptureCommon = """
        "cursor":{"type":"boolean","description":"Draw the cursor into the image."},
        "to":{"x-basin-fd":true,"description":"Where the image goes: {\"path\": \"/absolute/file.png\"}, \"inline\" for base64 PNG in the reply, or \"fd\" for raw pixels over a descriptor.","anyOf":[{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]},{"type":"string","enum":["inline","fd"]}],"examples":["inline"]},
        "scale":{"type":"number","exclusiveMinimum":0,"maximum":1,"description":"Scale the image down by this factor. Not with max_dimension."},
        "max_dimension":{"type":"integer","minimum":1,"description":"Scale the image down until its longer side is at most this many pixels. It never scales up. Not with scale."},
        "format":{"type":"string","enum":["png","png16"],"description":"png, the default, is 8-bit sRGB, and an HDR output is tone-mapped to SDR. png16 is 16-bit RGB in the output's own encoding, PQ on an HDR output, tagged with cICP and, when the output has HDR metadata, mDCV and cLLI. With fd, png16 sends raw abgr16161616f pixels."}
        """;

    private const string InputPath =
        " The input goes in before keybindings and gestures, where a physical device's input goes in. So a chord fires the compositor's bindings.";

    private const string SeatPath =
        " The input goes straight to the focused client, the same as a virtual keyboard or pointer. The compositor's keybindings do not see it. Prefer the input/ method unless the binding must not fire.";

    private const string WindowPoint =
        " With window, x and y are relative to that window's client area, and the call is refused when another window covers the point.";

    private const string WindowProperties = """
        "window":{"anyOf":[{"type":"integer","minimum":0},{"type":"string","minLength":1}],"description":"A window id from windows/list, an app_id, or part of a title. x and y are then relative to its client_geometry. A string of decimal digits is an id, because a JSON number above 2^53 loses digits in JavaScript."},
        "raise":{"type":"boolean","description":"With window, activate the window first when another window covers the point."}
        """;

    private static readonly Dictionary<string, IpcMethodInfo> Table = Build();

    public static IpcMethodInfo Of(string name) =>
        Table.TryGetValue(name, out var info)
            ? info
            : throw new InvalidOperationException($"The library method '{name}' has no schema.");

    public static IEnumerable<string> Names => Table.Keys;

    private static Dictionary<string, IpcMethodInfo> Build()
    {
        var table = new Dictionary<string, IpcMethodInfo>(StringComparer.Ordinal);
        void Add(string name, IpcMethodTraits traits, string description, string schema) =>
            table.Add(name, new IpcMethodInfo(description, schema, traits));

        const IpcMethodTraits ReadOnly = IpcMethodTraits.ReadOnly;
        const IpcMethodTraits Destructive = IpcMethodTraits.Destructive;
        const IpcMethodTraits Idempotent = IpcMethodTraits.Idempotent;
        const IpcMethodTraits None = IpcMethodTraits.None;

        Add(IpcMethodNames.Version, ReadOnly, "Return the control protocol version, the compositor's name and the basin version.", Empty);
        Add(IpcMethodNames.Methods, ReadOnly, "List the methods this compositor answers. With detail, each one carries its description, parameter schema and traits.", """
            {"type":"object","properties":{"detail":{"type":"boolean","description":"Return each method's description, schema and traits, not only its name."}}}
            """);
        Add(IpcMethodNames.Events, ReadOnly, "List the events this compositor can send.", Empty);
        Add(IpcMethodNames.Subscribe, None, "Receive the named events on this connection until it closes or unsubscribes.", """
            {"type":"object","properties":{"events":{"type":"array","items":{"type":"string"},"description":"Event names from ipc/events."},"options":{"type":"object","additionalProperties":{"type":"object"},"description":"Options for an event, keyed by its name, such as {\"diag/damage\": {\"stack\": true}}. A second subscribe to the same event replaces them."}},"required":["events"],"examples":[{"events":[]}]}
            """);
        Add(IpcMethodNames.Unsubscribe, None, "Stop the named events on this connection, or all of them when no names are given.", """
            {"type":"object","properties":{"events":{"type":"array","items":{"type":"string"}}}}
            """);

        Add(IpcMethodNames.SessionDescribe, ReadOnly, "Describe the session: the compositor, its backend and renderer, its sockets, the output count and its pid.", Empty);
        Add(IpcMethodNames.SessionQuit, Destructive, "Stop the compositor. Every client in the session loses its display.", Empty);
        Add(IpcMethodNames.SessionSettle, ReadOnly, "Wait until every output has presented what it was asked to draw and the compositor's own chrome has nothing due, and answer with the count of presented frames. It answers at once when nothing is pending, and failed at the timeout.", """
            {"type":"object","properties":{"timeout_ms":{"type":"integer","minimum":1,"maximum":3600000,"description":"The default is 5000."}},"examples":[{"timeout_ms":1000}]}
            """);

        Add(IpcMethodNames.OutputsList, ReadOnly, "List the outputs with their names, modes, scale, position, transform and power state.", Empty);
        Add(IpcMethodNames.OutputsTest, ReadOnly, "Ask whether an output configuration would apply, without applying it.", OutputEntries);
        Add(IpcMethodNames.OutputsApply, Destructive, "Apply an output configuration: mode, position, scale, transform, adaptive sync, or on and off.", OutputEntries);
        Add(IpcMethodNames.OutputsPower, Destructive, "Turn an output's display power on or off. The output keeps its place in the layout.", """
            {"type":"object","properties":{"output":{"type":"string","description":"The output name from outputs/list."},"on":{"type":"boolean"}},"required":["output","on"]}
            """);

        Add(IpcMethodNames.OutputsLook, Idempotent, "Apply a .cube 3D LUT to everything an output shows, in each source's encoded values before color conversion. Leave path out or empty to remove the look.", """
            {"type":"object","properties":{"output":{"type":"string","description":"The output name from outputs/list."},"path":{"type":"string","description":"An absolute path to a .cube file with LUT_3D_SIZE from 2 to 128."}},"required":["output"],"examples":[{"output":"DP-1","path":"/home/me/looks/warm.cube"}]}
            """);

        Add(IpcMethodNames.WindowsList, ReadOnly, "List the toplevel windows with their ids, app ids, titles, geometry, state, output and workspace. An X11 window also has x11_display and x11_density (native or low).", Empty);
        Add(IpcMethodNames.WindowsGet, ReadOnly, "Describe one window.", Id);
        Add(IpcMethodNames.WindowsStack, ReadOnly, "Return the window ids in stacking order, bottom to top.", Empty);
        Add(IpcMethodNames.WindowsActivate, Idempotent, "Ask the compositor to focus and raise a window. A compositor whose policy does not allow it answers refused.", Id);
        Add(IpcMethodNames.WindowsSetX11Density, Destructive, "Move an X11 application to the native or the low-density X server. It writes a [[launch]] rule into the compositor's configuration. When the compositor started the process, it closes the process's windows politely, waits up to 5 s for the process to exit and starts it again with the same argv, directory and environment. outcome is relaunched, applies-next-start (the rule is written and the process is left alone), still-running (the process did not exit, nothing was killed) or unchanged (it is on that server already).", """
            {"type":"object","properties":{"id":{"anyOf":[{"type":"integer","minimum":0},{"type":"string","minLength":1}],"description":"The window id of an X11 window from windows/list, its app_id, or part of its title. A string of decimal digits is an id, because a JSON number above 2^53 loses digits in JavaScript."},"density":{"enum":["native","low"],"description":"native, or low for the X server at scale 1 and 96 DPI that the compositor stretches."}},"required":["id","density"],"examples":[{"density":"low"}]}
            """);
        Add(IpcMethodNames.WindowsStats, ReadOnly, "Frame statistics for a window over the last second: commits and presents per second, the mean and 99th percentile present interval and commit-to-present latency in milliseconds, the output that presents it and its refresh. The first call starts collecting, so it can answer zeros.", Id);
        Add(IpcMethodNames.WindowsFrameLimit, Idempotent, "Limit how often a window is told to draw: frame callbacks, fifo and commit-timing release at this rate. 0 removes the limit. windows/get reports frame_limit.", """
            {"type":"object","properties":{"id":{"anyOf":[{"type":"integer","minimum":0},{"type":"string","minLength":1}],"description":"The window id from windows/list, an app_id, or part of a title. An active window wins a tie. A string of decimal digits is an id, because a JSON number above 2^53 loses digits in JavaScript."},"fps":{"type":"integer","minimum":0,"maximum":1000,"description":"Frames per second, or 0 for no limit."}},"required":["id","fps"],"examples":[{"fps":30}]}
            """);
        Add(IpcMethodNames.WindowsClose, Destructive, "Ask a window to close. The client can show a prompt or ignore the request.", Id);
        Add(IpcMethodNames.WindowsSetState, Idempotent, "Set or clear a window's maximized, minimized, fullscreen or borderless state. Name at least one.", """
            {"type":"object","properties":{"id":{"anyOf":[{"type":"integer","minimum":0},{"type":"string","minLength":1}],"description":"The window id from windows/list, an app_id, or part of a title. An active window wins a tie. A string of decimal digits is an id, because a JSON number above 2^53 loses digits in JavaScript."},"maximized":{"type":"boolean"},"minimized":{"type":"boolean"},"fullscreen":{"type":"boolean"},"no_border":{"type":"boolean"}},"required":["id"],"examples":[{"maximized":true}]}
            """);
        Add(IpcMethodNames.WindowsMove, Idempotent, "Ask the compositor to move a window's top-left corner to a point in layout coordinates. A tiler answers refused.", """
            {"type":"object","properties":{"id":{"anyOf":[{"type":"integer","minimum":0},{"type":"string","minLength":1}],"description":"The window id from windows/list, an app_id, or part of a title. An active window wins a tie. A string of decimal digits is an id, because a JSON number above 2^53 loses digits in JavaScript."},"x":{"type":"integer"},"y":{"type":"integer"}},"required":["id","x","y"]}
            """);
        Add(IpcMethodNames.WindowsResize, Idempotent, "Ask the compositor to resize a window. A tiler answers refused.", """
            {"type":"object","properties":{"id":{"anyOf":[{"type":"integer","minimum":0},{"type":"string","minLength":1}],"description":"The window id from windows/list, an app_id, or part of a title. An active window wins a tie. A string of decimal digits is an id, because a JSON number above 2^53 loses digits in JavaScript."},"width":{"type":"integer","minimum":1,"maximum":65535},"height":{"type":"integer","minimum":1,"maximum":65535}},"required":["id","width","height"]}
            """);
        Add(IpcMethodNames.WindowsSendToOutput, Idempotent, "Ask the compositor to move a window to another output.", """
            {"type":"object","properties":{"id":{"anyOf":[{"type":"integer","minimum":0},{"type":"string","minLength":1}],"description":"The window id from windows/list, an app_id, or part of a title. An active window wins a tie. A string of decimal digits is an id, because a JSON number above 2^53 loses digits in JavaScript."},"output":{"type":"string","description":"The output name from outputs/list."}},"required":["id","output"]}
            """);
        Add(IpcMethodNames.WindowsSendToWorkspace, Idempotent, "Ask the compositor to move a window to another workspace.", """
            {"type":"object","properties":{"id":{"anyOf":[{"type":"integer","minimum":0},{"type":"string","minLength":1}],"description":"The window id from windows/list, an app_id, or part of a title. An active window wins a tie. A string of decimal digits is an id, because a JSON number above 2^53 loses digits in JavaScript."},"workspace":{"anyOf":[{"type":"integer","minimum":0},{"type":"string","pattern":"^[0-9]+$"}],"description":"The workspace id from workspaces/list. A string of decimal digits is also accepted, because a JSON number above 2^53 loses digits in JavaScript."}},"required":["id","workspace"]}
            """);
        Add(IpcMethodNames.WindowsWait, ReadOnly, "Wait until a window with this app id or title exists, and describe it. It answers at once when one exists already, and failed at the timeout.", """
            {"type":"object","properties":{"app_id":{"type":"string","description":"The exact app id."},"title":{"type":"string","description":"A substring of the title."},"timeout_ms":{"type":"integer","minimum":1,"maximum":3600000,"description":"The default is 5000."}},"examples":[{"app_id":"none","timeout_ms":1}]}
            """);

        Add(IpcMethodNames.WindowsWaitIdle, ReadOnly, "Wait until a window, or every window, has drawn nothing for quiet_ms. A commit whose damage fits in an ignore_below square does not count, so a blinking cursor or a spinner does not keep a window busy. Answers failed at the timeout.", """
            {"type":"object","properties":{"id":{"anyOf":[{"type":"integer","minimum":0},{"type":"string","minLength":1}],"description":"The window id from windows/list, an app_id, or part of a title. The default is every window. A string of decimal digits is an id, because a JSON number above 2^53 loses digits in JavaScript."},"quiet_ms":{"type":"integer","minimum":1,"maximum":3600000,"description":"How long the window must draw nothing. The default is 300."},"ignore_below":{"type":"integer","minimum":0,"maximum":65535,"description":"A commit whose damage fits in a square of this many logical pixels does not count. The default is 64."},"timeout_ms":{"type":"integer","minimum":1,"maximum":3600000,"description":"The default is 5000."}},"examples":[{"quiet_ms":1,"timeout_ms":100}]}
            """);

        Add(IpcMethodNames.WorkspacesList, ReadOnly, "List the workspace groups, their outputs, and their workspaces with names, state and member windows.", Empty);
        Add(IpcMethodNames.WorkspacesActivate, Idempotent, "Ask the compositor to show a workspace.", WorkspaceId);
        Add(IpcMethodNames.WorkspacesDeactivate, Idempotent, "Ask the compositor to hide a workspace.", WorkspaceId);
        Add(IpcMethodNames.WorkspacesCreate, None, "Ask the compositor to create a workspace in a group.", """
            {"type":"object","properties":{"group":{"anyOf":[{"type":"integer","minimum":0},{"type":"string","pattern":"^[0-9]+$"}],"description":"The group id from workspaces/list. A string of decimal digits is also accepted, because a JSON number above 2^53 loses digits in JavaScript."},"name":{"type":"string"}},"required":["group","name"]}
            """);
        Add(IpcMethodNames.WorkspacesRemove, Destructive, "Ask the compositor to remove a workspace.", WorkspaceId);
        Add(IpcMethodNames.WorkspacesMove, None, "Ask the compositor to move a workspace to another group.", """
            {"type":"object","properties":{"id":{"anyOf":[{"type":"integer","minimum":0},{"type":"string","pattern":"^[0-9]+$"}],"description":"The workspace id from workspaces/list. A string of decimal digits is also accepted, because a JSON number above 2^53 loses digits in JavaScript."},"group":{"anyOf":[{"type":"integer","minimum":0},{"type":"string","pattern":"^[0-9]+$"}],"description":"The group id from workspaces/list. A string of decimal digits is also accepted, because a JSON number above 2^53 loses digits in JavaScript."}},"required":["id","group"]}
            """);

        Add(IpcMethodNames.IdleStatus, ReadOnly, "Return how long the session has been idle and whether idle is inhibited.", Empty);
        Add(IpcMethodNames.IdleActivity, None, "Count as user activity, the same as a key press, so idle timers restart.", Empty);
        Add(IpcMethodNames.IdleInhibit, None, "Stop the session going idle until idle/uninhibit or until this connection closes. Returns a token.", Empty);
        Add(IpcMethodNames.IdleUninhibit, Idempotent, "Release an idle inhibitor that this connection took.", """
            {"type":"object","properties":{"token":{"type":"string","description":"The token from idle/inhibit."}},"required":["token"]}
            """);

        Add(IpcMethodNames.LockStatus, ReadOnly, "Return whether the session is locked.", Empty);

        Add(IpcMethodNames.KeyboardKeymap, ReadOnly, "Describe the active keymap, and return its text when asked.", """
            {"type":"object","properties":{"text":{"type":"boolean","description":"Include the keymap text."},"fd":{"type":"boolean","x-basin-fd":true,"description":"Include a descriptor of the keymap."}}}
            """);

        AddInput(Add, IpcMethodNames.InputPointerMove, IpcMethodNames.InputPointerButton, IpcMethodNames.InputAxis, IpcMethodNames.InputKey,
            IpcMethodNames.InputChord, IpcMethodNames.InputText, IpcMethodNames.InputTouch, InputPath);
        AddInput(Add, IpcMethodNames.SeatPointerMove, IpcMethodNames.SeatPointerButton, IpcMethodNames.SeatAxis, IpcMethodNames.SeatKey,
            IpcMethodNames.SeatChord, IpcMethodNames.SeatText, IpcMethodNames.SeatTouch, SeatPath);

        Add(IpcMethodNames.CaptureOutput, ReadOnly, "Take a screenshot of one output as the display shows it. The default is the output under the pointer.", $$"""
            {"type":"object","properties":{"output":{"type":"string","description":"The output name from outputs/list."},{{CaptureCommon}}},"required":["to"]}
            """);
        Add(IpcMethodNames.CaptureWindow, ReadOnly, "Take a screenshot of one window.", $$"""
            {"type":"object","properties":{"id":{"anyOf":[{"type":"integer","minimum":0},{"type":"string","minLength":1}],"description":"The window id from windows/list, an app_id, or part of a title. An active window wins a tie. A string of decimal digits is an id, because a JSON number above 2^53 loses digits in JavaScript."},"client_only":{"type":"boolean","description":"Leave out the compositor's decorations."},{{CaptureCommon}}},"required":["id","to"]}
            """);
        Add(IpcMethodNames.CaptureRegion, ReadOnly, "Take a screenshot of a rectangle in layout coordinates.", $$"""
            {"type":"object","properties":{"x":{"type":"integer"},"y":{"type":"integer"},"width":{"type":"integer","minimum":1,"maximum":32768},"height":{"type":"integer","minimum":1,"maximum":32768},{{CaptureCommon}}},"required":["x","y","width","height","to"]}
            """);

        Add(IpcMethodNames.ProcessSpawn, None, "Start a program in the session, with no shell. It inherits WAYLAND_DISPLAY, DISPLAY, BASIN_X11_LOW_DENSITY_DISPLAY and BASIN_SOCKET, and it outlives the caller. The compositor tracks it: process/list, process/kill and process/log take the launch id this returns beside the pid.", """
            {"type":"object","properties":{"argv":{"type":"array","items":{"type":"string"},"minItems":1,"description":"The program and its arguments. The program is looked up on PATH."},"env":{"type":"object","additionalProperties":{"type":"string"},"description":"Variables to add to the environment."},"unset_env":{"type":"array","items":{"type":"string"},"description":"Variables to remove from the environment."},"cwd":{"type":"string","description":"An absolute working directory."},"log":{"type":"string","description":"An absolute file that receives stdout and stderr, appended to. process/log reads it back."},"x11_density":{"enum":["native","low"],"description":"The X server for an X11 program: native, or low for the server at scale 1 and 96 DPI that the compositor stretches. It overrides the compositor's [[launch]] rules. low starts it on the native server when the compositor has no low-density server or every output is at scale 1."}},"required":["argv"],"examples":[{"argv":["true"]}]}
            """);
        Add(IpcMethodNames.ProcessList, ReadOnly, "List the programs this compositor started: launch id, pid, argv, whether it runs, and its exit code or signal.", Empty);
        Add(IpcMethodNames.ProcessKill, Destructive, "Send SIGTERM to a launch's process group, then SIGKILL after grace_ms if anything in the group is still alive.", """
            {"type":"object","properties":{"launch_id":{"type":"integer","minimum":1,"description":"The launch id from process/spawn or process/list."},"grace_ms":{"type":"integer","minimum":0,"maximum":600000,"description":"The default is 2000."}},"required":["launch_id"]}
            """);
        Add(IpcMethodNames.ProcessLog, ReadOnly, "Return the last lines of a launch's log, when process/spawn named one.", """
            {"type":"object","properties":{"launch_id":{"type":"integer","minimum":1,"description":"The launch id from process/spawn or process/list."},"lines":{"type":"integer","minimum":1,"maximum":10000,"description":"The default is 50."}},"required":["launch_id"]}
            """);

        Add(IpcMethodNames.ClipboardRead, ReadOnly, "Read the text on the clipboard or the primary selection. With no selection, types is empty. With no text type offered, text is null and types lists what is offered.", """
            {"type":"object","properties":{"kind":{"type":"string","enum":["clipboard","primary"],"description":"The default is clipboard."},"mime":{"type":"string","description":"A text type to ask for. The default is the first of text/plain;charset=utf-8, UTF8_STRING, text/plain, STRING and TEXT that is offered."},"timeout_ms":{"type":"integer","minimum":1,"maximum":120000,"description":"How long the owner has to write. The default is 2000."},"max_bytes":{"type":"integer","minimum":1,"maximum":16777216,"description":"The default is 1048576."}}}
            """);

        Add(IpcMethodNames.ClipboardWrite, None, "Put text on the clipboard or the primary selection, offered as text/plain;charset=utf-8, text/plain and UTF8_STRING. It stays there until another owner replaces it.", """
            {"type":"object","properties":{"text":{"type":"string"},"kind":{"type":"string","enum":["clipboard","primary"],"description":"The default is clipboard."}},"required":["text"]}
            """);

        Add(IpcMethodNames.DevicesList, ReadOnly, "List the input devices: id, name, type, capabilities, sysname, vendor, product, bus, the output it maps to, and the indexes of the config rules it matched.", Empty);
        Add(IpcMethodNames.DevicesGet, ReadOnly, "Return every setting the device supports: its current value, its default, where the value came from (default, config or runtime), and the value words it takes.", """
            {"type":"object","properties":{"device":{"anyOf":[{"type":"integer","minimum":0},{"type":"string","pattern":"^[0-9]+$"}],"description":"The device id from devices/list."}},"required":["device"]}
            """);
        Add(IpcMethodNames.DevicesSet, Idempotent, "Set input settings at run time on one device or on every device of a type. The values hold until devices/reset, and the config file does not change. Returns ok, unsupported or invalid per device and key.", """
            {"type":"object","properties":{"device":{"anyOf":[{"type":"integer","minimum":0},{"type":"string","pattern":"^[0-9]+$"}],"description":"The device id from devices/list. Give device or type."},"type":{"type":"string","enum":["keyboard","pointer","touchpad","touch","tablet_tool","tablet_pad","switch"],"description":"Every device of this type. keyboard means every device with keys."},"settings":{"type":"object","description":"Setting keys to values, for example {\"tap\": true, \"accel_speed\": -0.3, \"scroll_method\": \"two_finger\", \"layout\": \"us,de\"}. devices/get lists the keys and value words a device takes.","additionalProperties":{"anyOf":[{"type":"string"},{"type":"number"},{"type":"boolean"},{"type":"array","items":{"type":"number"}}]}}},"required":["settings"],"examples":[{"type":"touchpad","settings":{"tap":true}}]}
            """);
        Add(IpcMethodNames.DevicesReset, Idempotent, "Remove run-time input settings from a device, so the config value or the default comes back. With no keys, every run-time setting on the device.", """
            {"type":"object","properties":{"device":{"anyOf":[{"type":"integer","minimum":0},{"type":"string","pattern":"^[0-9]+$"}],"description":"The device id from devices/list."},"keys":{"type":"array","items":{"type":"string"},"description":"Setting keys, for example [\"tap\"]."}},"required":["device"]}
            """);

        Add(IpcMethodNames.DiagTree, ReadOnly, "Return the scene graph as nodes in depth order: depth, kind, name, path, position, size, whether it is enabled, and the details (clip, buffer, source box, backdrop effect and region, deformer).", Empty);
        Add(IpcMethodNames.DiagFrames, ReadOnly, "Return each window frame with its window id, draws, animation, bounds and the state and interaction it last drew with, and the frame clock's sink count (-1 when unknown).", Empty);
        Add(IpcMethodNames.DiagDamageTrace, Idempotent, "Print a DAMAGE line on the compositor's report for every scene damage, with the path of the node it came from. stack also prints the call stack the first time each path is damaged.", """
            {"type":"object","properties":{"mode":{"type":"string","enum":["on","stack","off"]}},"required":["mode"]}
            """);
        Add(IpcMethodNames.DiagFrameTrace, Idempotent, "Print a FRAMEDRAW line on the compositor's report each time a tracked window frame draws, with its state and interaction.", """
            {"type":"object","properties":{"on":{"type":"boolean"}},"required":["on"]}
            """);
        Add(IpcMethodNames.DiagRecord, None, "Record a region of the next presented frames into one PNG, one band per frame, with accepted overlay planes blended in. Replies when the frames are recorded, or at the timeout, with per-frame stats (position, mean luma, peak luma of the brightest 2%, pixels changed since the last frame, planes) and their ranges.", """
            {"type":"object","properties":{"x":{"type":"integer"},"y":{"type":"integer"},"width":{"type":"integer","minimum":1},"height":{"type":"integer","minimum":1},"frames":{"type":"integer","minimum":1,"maximum":600},"path":{"type":"string","description":"An absolute path for the PNG. Without one, the PNG comes back in the reply as base64 png."},"max_dimension":{"type":"integer","minimum":0,"description":"Scale each band down until its longer side is at most this many pixels. The stats are measured at full size. 0, the default, keeps full size."},"timeout_ms":{"type":"integer","minimum":1,"description":"Reply with the frames recorded so far after this long. The default is 10000."},"output":{"type":"string","description":"The output name from outputs/list. The default is the first output."}},"required":["x","y","width","height","frames"]}
            """);
        Add(IpcMethodNames.DiagRecordWindow, None, "Record a window and its frame in the next presented frames, as diag/record does. The region follows the window as it moves.", """
            {"type":"object","properties":{"window":{"anyOf":[{"type":"integer","minimum":0},{"type":"string"}],"description":"A window id from windows/list, an app_id, or part of a title (an active window wins a tie); x11:CLASS for an X11 window; NAME/popup or NAME/popup:N for a popup or menu of that window; layer:NAMESPACE for a layer surface; node:PATH for a scene node from diag/tree."},"frames":{"type":"integer","minimum":1,"maximum":600},"path":{"type":"string","description":"An absolute path for the PNG. Without one, the PNG comes back in the reply as base64 png."},"max_dimension":{"type":"integer","minimum":0,"description":"Scale each band down until its longer side is at most this many pixels. The stats are measured at full size. 0, the default, keeps full size."},"timeout_ms":{"type":"integer","minimum":1,"description":"Reply with the frames recorded so far after this long. The default is 10000."},"margin":{"type":"integer","minimum":0,"description":"Logical pixels around the frame. The default is 24."}},"required":["window","frames"]}
            """);
        Add(IpcMethodNames.DiagParts, ReadOnly, "Return the window's id, app_id and title, and each part of its frame (title, buttons, edges) with its box in layout coordinates.", """
            {"type":"object","properties":{"window":{"anyOf":[{"type":"integer","minimum":0},{"type":"string"}],"description":"A window id from windows/list, an app_id, or part of a title (an active window wins a tie); x11:CLASS for an X11 window; NAME/popup or NAME/popup:N for a popup or menu of that window; layer:NAMESPACE for a layer surface; node:PATH for a scene node from diag/tree."}},"required":["window"]}
            """);
        Add(IpcMethodNames.DiagElement, ReadOnly, "Return the box of a named element of the compositor's own UI, such as a button or a check box of its chrome, in layout coordinates. The newest UI surface answers first. Press the center with input/pointer-move and input/pointer-button.", """
            {"type":"object","properties":{"name":{"type":"string","minLength":1,"description":"The name that the UI toolkit gives the element, for example x:Name in Avalonia."}},"required":["name"]}
            """);
        Add(IpcMethodNames.DiagClick, None, "Click a part of a window's frame, such as close or maximize, with synthetic input at the center of the part. Replies once a frame has presented after the click and the window stopped committing, or at the timeout with settled false.", """
            {"type":"object","properties":{"window":{"anyOf":[{"type":"integer","minimum":0},{"type":"string"}],"description":"A window id from windows/list, an app_id, or part of a title (an active window wins a tie); x11:CLASS for an X11 window; NAME/popup or NAME/popup:N for a popup or menu of that window; layer:NAMESPACE for a layer surface; node:PATH for a scene node from diag/tree."},"part":{"type":"string","description":"A part name from diag/parts."},"button":{"type":"string","enum":["left","right","middle"]},"timeout_ms":{"type":"integer","minimum":1,"description":"How long to wait for the result to be shown. The default is 2000."}},"required":["window","part"]}
            """);
        Add(IpcMethodNames.DiagDrag, None, "Drag a part of a window's frame with synthetic input: press, move in steps, release. Each step waits for a presented frame. The default part is title, so the default drag moves the window. Replies once the result is shown, as diag/click does.", """
            {"type":"object","properties":{"window":{"anyOf":[{"type":"integer","minimum":0},{"type":"string"}],"description":"A window id from windows/list, an app_id, or part of a title (an active window wins a tie); x11:CLASS for an X11 window; NAME/popup or NAME/popup:N for a popup or menu of that window; layer:NAMESPACE for a layer surface; node:PATH for a scene node from diag/tree."},"dx":{"type":"number"},"dy":{"type":"number"},"part":{"type":"string","description":"A part name from diag/parts. The default is title."},"steps":{"type":"integer","minimum":1,"maximum":600,"description":"The default is 20."},"interval_ms":{"type":"integer","minimum":1,"maximum":1000,"description":"The longest wait for a presented frame before the next step. The default is 50."},"timeout_ms":{"type":"integer","minimum":1,"description":"How long to wait for the result to be shown after the release. The default is 2000."}},"required":["window","dx","dy"]}
            """);
        Add(IpcMethodNames.DiagInputTrace, Idempotent, "Subscribe the caller to diag/input: pointer enter, leave, buttons, axis, keyboard focus and grabs, with the window and coordinates. motion adds pointer motion, once a frame at most.", """
            {"type":"object","properties":{"mode":{"type":"string","enum":["on","motion","off"]}},"required":["mode"]}
            """);
        Add(IpcMethodNames.DiagPlanesState, ReadOnly, "Return, for each output, the nodes on overlay planes in the last frame and the candidates kept in the composite with the reason (blend-space, backend-refused, demoted and so on), or why the output tried no planes at all. Each entry's box is in scene coordinates; an entry on a plane also has plane_box in output physical pixels.", Empty);
        Add(IpcMethodNames.DiagPlaneTrace, Idempotent, "Subscribe the caller to diag/plane, which sends an output's diag/planes-state entry each time it changes.", """
            {"type":"object","properties":{"on":{"type":"boolean"}},"required":["on"]}
            """);
        Add(IpcMethodNames.DiagCommitTrace, Idempotent, "Subscribe the caller to diag/commit for a target: each surface commit (buffer size and format, damage boxes, frame callbacks, presentation feedback) and each time its frame callbacks fire.", """
            {"type":"object","properties":{"on":{"type":"boolean"},"target":{"type":"string","minLength":1,"description":"A window id, an app_id, part of a title, or * for every surface."}},"required":["on","target"]}
            """);
        Add(IpcMethodNames.DiagCompare, ReadOnly, "Compare the window and its frame as shown now (or in the next presented frame) with a reference PNG. A pixel differs when a channel differs by more than channel; the compare passes when at most pixels differ. Returns the count, the largest channel difference and the box of the change, and writes a diff PNG (changed pixels red over grey) when asked. A size mismatch is a failed result, not an error.", """
            {"type":"object","properties":{"window":{"anyOf":[{"type":"integer","minimum":0},{"type":"string"}],"description":"A window id from windows/list, an app_id, or part of a title (an active window wins a tie); x11:CLASS for an X11 window; NAME/popup or NAME/popup:N for a popup or menu of that window; layer:NAMESPACE for a layer surface; node:PATH for a scene node from diag/tree."},"reference":{"type":"string","description":"An absolute path to the reference PNG, such as one diag/record-window wrote."},"channel":{"type":"integer","minimum":0,"maximum":255,"description":"The default is 2."},"pixels":{"type":"integer","minimum":0,"description":"The default is 0."},"diff_path":{"type":"string"},"inline":{"type":"boolean","description":"Return the diff PNG in the reply as base64 png."},"margin":{"type":"integer","minimum":0,"description":"Logical pixels around the frame, as in diag/record-window. The default is 24."},"timeout_ms":{"type":"integer","minimum":1,"description":"The default is 2000."}},"required":["window","reference"]}
            """);
        Add(IpcMethodNames.DiagSyncobj, ReadOnly, "Return the counters of wp_linux_drm_syncobj since the global was created: managed and explicit commits, acquire points that parked, could not park, came late or could not be exported, release points that waited for renderer reads, release points of buffers that a later synchronized commit replaced before they were shown, and release points of commits discarded before they applied.", Empty);
        Add(IpcMethodNames.DiagTargets, ReadOnly, "List every target a window selector can reach, with its kind (window, x11, popup, layer), its selector, window id, app_id, output and box. A scene node is reachable as node:PATH with a path from diag/tree.", Empty);
        Add(IpcMethodNames.DiagPlanes, Idempotent, "Allow or stop overlay planes and direct scanout on every output, to compare a frame with and without them.", """
            {"type":"object","properties":{"on":{"type":"boolean"}},"required":["on"]}
            """);

        Add(IpcMethodNames.ApprovalAnswer, None, "Answer an approval request from approval/requested. Only the first connection subscribed to approval/requested can answer.", """
            {"type":"object","properties":{"id":{"type":"integer","minimum":1,"description":"The id from the approval/requested event."},"answer":{"type":"string","enum":["allow_once","allow_run","deny"],"description":"allow_run also allows later calls to the same method for the rest of the run."}},"required":["id","answer"]}
            """);

        return table;
    }

    private static void AddInput(
        Action<string, IpcMethodTraits, string, string> add,
        string move,
        string button,
        string axis,
        string key,
        string chord,
        string text,
        string touch,
        string path)
    {
        add(move, IpcMethodTraits.None, "Move the pointer to a point in layout coordinates, or in a window's client area with window." + WindowPoint + path, $$"""
            {"type":"object","properties":{"x":{"type":"number"},"y":{"type":"number"},{{WindowProperties}}},"required":["x","y"]}
            """);
        add(button, IpcMethodTraits.None, "Press or release a pointer button. With no pressed, it clicks: a press and then a release. With x and y, the pointer moves there first." + WindowPoint + path, $$"""
            {"type":"object","properties":{"button":{"anyOf":[{"type":"string","enum":["left","right","middle","side","extra"]},{"type":"integer","minimum":0}],"description":"A button name or an evdev code."},"pressed":{"type":"boolean"},"x":{"type":"number"},"y":{"type":"number"},{{WindowProperties}}},"required":["button"]}
            """);
        add(axis, IpcMethodTraits.None, "Scroll. A positive value scrolls down or right. With x and y, the pointer moves there first." + WindowPoint + path, $$"""
            {"type":"object","properties":{"value":{"type":"number"},"axis":{"type":"string","enum":["vertical","horizontal"]},"source":{"type":"string","enum":["wheel","finger","continuous","wheel-tilt"]},"x":{"type":"number"},"y":{"type":"number"},{{WindowProperties}}},"required":["value"]}
            """);
        add(key, IpcMethodTraits.None, "Press or release a key by its evdev code. With no pressed, it taps the key." + path, """
            {"type":"object","properties":{"code":{"type":"integer","minimum":0,"maximum":767,"description":"An evdev keycode, for example 30 for A or 28 for Enter."},"pressed":{"type":"boolean"}},"required":["code"]}
            """);
        add(chord, IpcMethodTraits.None, "Press a chord such as Super+Shift+c in order, then release it in reverse." + path, """
            {"type":"object","properties":{"chord":{"type":"string","description":"Modifiers and one key joined by +, with xkb keysym names, for example Alt+n or Super+Return."}},"required":["chord"],"examples":[{"chord":"Super"}]}
            """);
        add(text, IpcMethodTraits.None, "Type text. When the focused client has an enabled text input, the whole text goes to it as one commit, so any character works. Otherwise it types through the active keymap, with Shift where it needs it, and a character the keymap cannot produce fails with nothing typed. Returns the path taken." + path, """
            {"type":"object","properties":{"text":{"type":"string"},"via":{"type":"string","enum":["auto","keymap","text-input"],"description":"Force one path. The default is auto."}},"required":["text"]}
            """);
        add(touch, IpcMethodTraits.None, "Send one touch event. Send frame after a group of down, motion and up events." + path, """
            {"type":"object","properties":{"kind":{"type":"string","enum":["down","motion","up","frame","cancel"]},"id":{"type":"integer","description":"The touch point id."},"x":{"type":"number","description":"Needed for down and motion."},"y":{"type":"number","description":"Needed for down and motion."},"time_ms":{"type":"integer","minimum":0,"maximum":4294967295,"description":"The event time in milliseconds of the monotonic clock, as a device stamps it. The default is the time the call arrives. A script that replays a gesture names it, so the gesture's speed does not depend on how fast the calls arrive."}},"required":["kind"],"examples":[{"kind":"frame"}]}
            """);
    }
}
