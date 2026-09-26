namespace Basin.Ipc;

internal static class IpcSchemas
{
    private const string Empty = """{"type":"object","properties":{}}""";

    private const string Id = """
        {"type":"object","properties":{"id":{"anyOf":[{"type":"integer","minimum":0},{"type":"string","pattern":"^[0-9]+$"}],"description":"The window id from windows/list. A string of decimal digits is also accepted, because a JSON number above 2^53 loses digits in JavaScript."}},"required":["id"]}
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
        "max_dimension":{"type":"integer","minimum":1,"description":"Scale the image down until its longer side is at most this many pixels. It never scales up. Not with scale."}
        """;

    private const string InputPath =
        " The input goes in before keybindings and gestures, where a physical device's input goes in. So a chord fires the compositor's bindings.";

    private const string SeatPath =
        " The input goes straight to the focused client, the same as a virtual keyboard or pointer. The compositor's keybindings do not see it. Prefer the input/ method unless the binding must not fire.";

    private static readonly Dictionary<string, IpcMethodInfo> Table = Build();

    public static IpcMethodInfo Of(string name) =>
        Table.TryGetValue(name, out var info)
            ? info
            : throw new InvalidOperationException($"the library method '{name}' has no schema");

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
            {"type":"object","properties":{"events":{"type":"array","items":{"type":"string"},"description":"Event names from ipc/events."}},"required":["events"],"examples":[{"events":[]}]}
            """);
        Add(IpcMethodNames.Unsubscribe, None, "Stop the named events on this connection, or all of them when no names are given.", """
            {"type":"object","properties":{"events":{"type":"array","items":{"type":"string"}}}}
            """);

        Add(IpcMethodNames.SessionDescribe, ReadOnly, "Describe the session: the compositor, its backend and renderer, its sockets, the output count and its pid.", Empty);
        Add(IpcMethodNames.SessionQuit, Destructive, "Stop the compositor. Every client in the session loses its display.", Empty);

        Add(IpcMethodNames.OutputsList, ReadOnly, "List the outputs with their names, modes, scale, position, transform and power state.", Empty);
        Add(IpcMethodNames.OutputsTest, ReadOnly, "Ask whether an output configuration would apply, without applying it.", OutputEntries);
        Add(IpcMethodNames.OutputsApply, Destructive, "Apply an output configuration: mode, position, scale, transform, adaptive sync, or on and off.", OutputEntries);
        Add(IpcMethodNames.OutputsPower, Destructive, "Turn an output's display power on or off. The output keeps its place in the layout.", """
            {"type":"object","properties":{"output":{"type":"string","description":"The output name from outputs/list."},"on":{"type":"boolean"}},"required":["output","on"]}
            """);

        Add(IpcMethodNames.WindowsList, ReadOnly, "List the toplevel windows with their ids, app ids, titles, geometry, state, output and workspace.", Empty);
        Add(IpcMethodNames.WindowsGet, ReadOnly, "Describe one window.", Id);
        Add(IpcMethodNames.WindowsStack, ReadOnly, "Return the window ids in stacking order, bottom to top.", Empty);
        Add(IpcMethodNames.WindowsActivate, Idempotent, "Ask the compositor to focus and raise a window. A compositor whose policy does not allow it answers refused.", Id);
        Add(IpcMethodNames.WindowsClose, Destructive, "Ask a window to close. The client can show a prompt or ignore the request.", Id);
        Add(IpcMethodNames.WindowsSetState, Idempotent, "Set or clear a window's maximized, minimized, fullscreen or borderless state. Name at least one.", """
            {"type":"object","properties":{"id":{"anyOf":[{"type":"integer","minimum":0},{"type":"string","pattern":"^[0-9]+$"}],"description":"The window id from windows/list. A string of decimal digits is also accepted, because a JSON number above 2^53 loses digits in JavaScript."},"maximized":{"type":"boolean"},"minimized":{"type":"boolean"},"fullscreen":{"type":"boolean"},"no_border":{"type":"boolean"}},"required":["id"],"examples":[{"maximized":true}]}
            """);
        Add(IpcMethodNames.WindowsMove, Idempotent, "Ask the compositor to move a window's top-left corner to a point in layout coordinates. A tiler answers refused.", """
            {"type":"object","properties":{"id":{"anyOf":[{"type":"integer","minimum":0},{"type":"string","pattern":"^[0-9]+$"}],"description":"The window id from windows/list. A string of decimal digits is also accepted, because a JSON number above 2^53 loses digits in JavaScript."},"x":{"type":"integer"},"y":{"type":"integer"}},"required":["id","x","y"]}
            """);
        Add(IpcMethodNames.WindowsResize, Idempotent, "Ask the compositor to resize a window. A tiler answers refused.", """
            {"type":"object","properties":{"id":{"anyOf":[{"type":"integer","minimum":0},{"type":"string","pattern":"^[0-9]+$"}],"description":"The window id from windows/list. A string of decimal digits is also accepted, because a JSON number above 2^53 loses digits in JavaScript."},"width":{"type":"integer","minimum":1,"maximum":65535},"height":{"type":"integer","minimum":1,"maximum":65535}},"required":["id","width","height"]}
            """);
        Add(IpcMethodNames.WindowsSendToOutput, Idempotent, "Ask the compositor to move a window to another output.", """
            {"type":"object","properties":{"id":{"anyOf":[{"type":"integer","minimum":0},{"type":"string","pattern":"^[0-9]+$"}],"description":"The window id from windows/list. A string of decimal digits is also accepted, because a JSON number above 2^53 loses digits in JavaScript."},"output":{"type":"string","description":"The output name from outputs/list."}},"required":["id","output"]}
            """);
        Add(IpcMethodNames.WindowsSendToWorkspace, Idempotent, "Ask the compositor to move a window to another workspace.", """
            {"type":"object","properties":{"id":{"anyOf":[{"type":"integer","minimum":0},{"type":"string","pattern":"^[0-9]+$"}],"description":"The window id from windows/list. A string of decimal digits is also accepted, because a JSON number above 2^53 loses digits in JavaScript."},"workspace":{"anyOf":[{"type":"integer","minimum":0},{"type":"string","pattern":"^[0-9]+$"}],"description":"The workspace id from workspaces/list. A string of decimal digits is also accepted, because a JSON number above 2^53 loses digits in JavaScript."}},"required":["id","workspace"]}
            """);
        Add(IpcMethodNames.WindowsWait, ReadOnly, "Wait until a window with this app id or title exists, and describe it. It answers at once when one exists already, and failed at the timeout.", """
            {"type":"object","properties":{"app_id":{"type":"string","description":"The exact app id."},"title":{"type":"string","description":"A substring of the title."},"timeout_ms":{"type":"integer","minimum":1,"maximum":3600000,"description":"The default is 5000."}},"examples":[{"app_id":"none","timeout_ms":1}]}
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
            {"type":"object","properties":{"id":{"anyOf":[{"type":"integer","minimum":0},{"type":"string","pattern":"^[0-9]+$"}],"description":"The window id from windows/list. A string of decimal digits is also accepted, because a JSON number above 2^53 loses digits in JavaScript."},"client_only":{"type":"boolean","description":"Leave out the compositor's decorations."},{{CaptureCommon}}},"required":["id","to"]}
            """);
        Add(IpcMethodNames.CaptureRegion, ReadOnly, "Take a screenshot of a rectangle in layout coordinates.", $$"""
            {"type":"object","properties":{"x":{"type":"integer"},"y":{"type":"integer"},"width":{"type":"integer","minimum":1,"maximum":32768},"height":{"type":"integer","minimum":1,"maximum":32768},{{CaptureCommon}}},"required":["x","y","width","height","to"]}
            """);

        Add(IpcMethodNames.ProcessSpawn, None, "Start a program in the session, with no shell. It inherits WAYLAND_DISPLAY, DISPLAY and BASIN_SOCKET, and it outlives the caller. Returns the pid.", """
            {"type":"object","properties":{"argv":{"type":"array","items":{"type":"string"},"minItems":1,"description":"The program and its arguments. The program is looked up on PATH."},"env":{"type":"object","additionalProperties":{"type":"string"},"description":"Variables to add to the environment."},"cwd":{"type":"string","description":"An absolute working directory."}},"required":["argv"],"examples":[{"argv":["true"]}]}
            """);

        Add(IpcMethodNames.ClipboardRead, ReadOnly, "Read the text on the clipboard or the primary selection. With no selection, types is empty. With no text type offered, text is null and types lists what is offered.", """
            {"type":"object","properties":{"kind":{"type":"string","enum":["clipboard","primary"],"description":"The default is clipboard."},"mime":{"type":"string","description":"A text type to ask for. The default is the first of text/plain;charset=utf-8, UTF8_STRING, text/plain, STRING and TEXT that is offered."},"timeout_ms":{"type":"integer","minimum":1,"maximum":120000,"description":"How long the owner has to write. The default is 2000."},"max_bytes":{"type":"integer","minimum":1,"maximum":16777216,"description":"The default is 1048576."}}}
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
        add(move, IpcMethodTraits.None, "Move the pointer to a point in layout coordinates." + path, """
            {"type":"object","properties":{"x":{"type":"number"},"y":{"type":"number"}},"required":["x","y"]}
            """);
        add(button, IpcMethodTraits.None, "Press or release a pointer button. With no pressed, it clicks: a press and then a release." + path, """
            {"type":"object","properties":{"button":{"anyOf":[{"type":"string","enum":["left","right","middle","side","extra"]},{"type":"integer","minimum":0}],"description":"A button name or an evdev code."},"pressed":{"type":"boolean"}},"required":["button"]}
            """);
        add(axis, IpcMethodTraits.None, "Scroll. A positive value scrolls down or right." + path, """
            {"type":"object","properties":{"value":{"type":"number"},"axis":{"type":"string","enum":["vertical","horizontal"]},"source":{"type":"string","enum":["wheel","finger","continuous","wheel-tilt"]}},"required":["value"]}
            """);
        add(key, IpcMethodTraits.None, "Press or release a key by its evdev code. With no pressed, it taps the key." + path, """
            {"type":"object","properties":{"code":{"type":"integer","minimum":0,"maximum":767,"description":"An evdev keycode, for example 30 for A or 28 for Enter."},"pressed":{"type":"boolean"}},"required":["code"]}
            """);
        add(chord, IpcMethodTraits.None, "Press a chord such as Super+Shift+c in order, then release it in reverse." + path, """
            {"type":"object","properties":{"chord":{"type":"string","description":"Modifiers and one key joined by +, with xkb keysym names, for example Alt+n or Super+Return."}},"required":["chord"],"examples":[{"chord":"Super"}]}
            """);
        add(text, IpcMethodTraits.None, "Type text through the active keymap, with Shift where it needs it. A character the keymap cannot produce fails, and nothing is typed." + path, """
            {"type":"object","properties":{"text":{"type":"string"}},"required":["text"]}
            """);
        add(touch, IpcMethodTraits.None, "Send one touch event. Send frame after a group of down, motion and up events." + path, """
            {"type":"object","properties":{"kind":{"type":"string","enum":["down","motion","up","frame","cancel"]},"id":{"type":"integer","description":"The touch point id."},"x":{"type":"number","description":"Needed for down and motion."},"y":{"type":"number","description":"Needed for down and motion."}},"required":["kind"],"examples":[{"kind":"frame"}]}
            """);
    }
}
