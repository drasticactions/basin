using System.Reflection;
using System.Text.Json;
using Basin;
using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Ipc;
using Basin.Tests;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Xunit;

namespace BasinMcp.Tests;

public sealed class McpToolTests
{
    private static readonly string[] BridgeTools = [McpBridgeStatus.Name, McpWaitEvent.Name];

    private static IEnumerable<string> LibraryNames() =>
        typeof(IpcMethodNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!);

    private static string Text(CallToolResult result) => string.Join('\n', result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    private static TestToplevelModel Model(McpHarness harness) => (TestToplevelModel)harness.Rig!.Services.Require<IToplevelModel>();

    private static IEnumerable<string> Expected(IpcServer server) =>
        server.Methods.Names
            .Where(name => !McpToolTable.Excluded.Contains(name))
            .Select(McpToolName.FromMethod)
            .Concat(BridgeTools)
            .Order(StringComparer.Ordinal);

    [Fact]
    public void Tools_are_the_method_table_minus_the_subscription_methods_plus_the_bridge_tools()
    {
        using (var full = McpHarness.Full())
        {
            var tools = full.ListTools();
            Assert.Equal(Expected(full.Rig!.Server), tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
            Assert.DoesNotContain("ipc_subscribe", tools.Select(tool => tool.Name));
            Assert.DoesNotContain("ipc_methods", tools.Select(tool => tool.Name));
            Assert.Equal(0, full.ListChanged);
        }

        using var bare = McpHarness.Create(path => new IpcTestRig(path: path, listen: true));
        var bareTools = bare.ListTools();
        Assert.Equal(Expected(bare.Rig!.Server), bareTools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
        Assert.Contains("ipc_version", bareTools.Select(tool => tool.Name));
    }

    [Fact]
    public void A_consumer_method_with_no_info_is_permissive_and_described_by_its_line()
    {
        using var harness = McpHarness.Full(register: server =>
            server.Methods.Register("test/plain", (ref IpcParams _, IpcReply reply) =>
            {
                reply.Result.WriteStartObject();
                reply.Result.WriteString("said"u8, "hello");
                reply.Result.WriteEndObject();
            }, line: "plain {word}"));
        var tool = Assert.Single(harness.ListTools(), tool => tool.Name == "test_plain");
        Assert.Equal("plain {word}", tool.Description);
        Assert.Equal("""{"type":"object"}""", tool.InputSchema.GetRawText());
        Assert.True(tool.Annotations!.DestructiveHint);
        Assert.False(tool.Annotations.ReadOnlyHint);

        var result = harness.Call("test_plain", """{"word":"x"}""");
        Assert.NotEqual(true, result.IsError);
        Assert.Equal("hello", result.StructuredContent!.Value.GetProperty("said").GetString());
    }

    [Fact]
    public void The_name_mapping_reverses_for_every_library_name()
    {
        foreach (var name in LibraryNames())
        {
            var tool = McpToolName.FromMethod(name);
            Assert.Matches("^[a-zA-Z0-9_-]{1,64}$", tool);
            Assert.Equal(name, McpToolName.ToMethod(tool));
        }

        Assert.Equal("windows_set-state", McpToolName.FromMethod("windows/set-state"));
    }

    [Fact]
    public void Annotations_follow_the_traits()
    {
        using var harness = McpHarness.Full();
        var server = harness.Rig!.Server;
        foreach (var tool in harness.ListTools().Where(tool => !BridgeTools.Contains(tool.Name)))
        {
            var method = McpToolName.ToMethod(tool.Name);
            Assert.True(server.Methods.TryGetInfo(method, out var info), method);
            var annotations = tool.Annotations!;
            Assert.Equal((info.Traits & IpcMethodTraits.ReadOnly) != 0, annotations.ReadOnlyHint);
            Assert.Equal((info.Traits & IpcMethodTraits.Destructive) != 0, annotations.DestructiveHint);
            Assert.Equal((info.Traits & IpcMethodTraits.Idempotent) != 0, annotations.IdempotentHint);
            Assert.Equal(method == IpcMethodNames.ProcessSpawn, annotations.OpenWorldHint);
            Assert.Equal(info.Description, tool.Description);
        }
    }

    [Fact]
    public void No_tool_schema_carries_a_descriptor_property()
    {
        using var harness = McpHarness.Full();
        var tools = harness.ListTools();
        foreach (var tool in tools)
        {
            Assert.DoesNotContain("x-basin-fd", tool.InputSchema.GetRawText(), StringComparison.Ordinal);
            Assert.False(tool.InputSchema.TryGetProperty("examples", out _), tool.Name);
        }

        var keymap = Assert.Single(tools, tool => tool.Name == "keyboard_keymap");
        Assert.False(keymap.InputSchema.GetProperty("properties").TryGetProperty("fd", out _));
        var capture = Assert.Single(tools, tool => tool.Name == "capture_output");
        var properties = capture.InputSchema.GetProperty("properties");
        Assert.False(properties.TryGetProperty("to", out _));
        Assert.True(properties.TryGetProperty("path", out _));
        Assert.False(capture.InputSchema.TryGetProperty("required", out _));
    }

    [Fact]
    public void Read_only_allow_and_deny_hide_what_they_should()
    {
        using (var readOnly = McpHarness.Full(new McpBridgeOptions { Filter = new McpMethodFilter(true, [], []) }))
        {
            var tools = readOnly.ListTools();
            Assert.All(tools, tool => Assert.True(tool.Annotations!.ReadOnlyHint, tool.Name));
            Assert.Contains("windows_list", tools.Select(tool => tool.Name));
            Assert.Contains("capture_output", tools.Select(tool => tool.Name));
            Assert.DoesNotContain("windows_close", tools.Select(tool => tool.Name));
            var hidden = Assert.Throws<McpProtocolException>(() => readOnly.Call("windows_close", """{"id":1}"""));
            Assert.Equal(McpErrorCode.InvalidParams, hidden.ErrorCode);

            var thenRefused = readOnly.Call("wait_event", """{"events":["window/added"],"then":{"method":"process/spawn","params":{"argv":["true"]}},"timeout_ms":50}""");
            Assert.True(thenRefused.IsError);
            Assert.StartsWith(IpcErrorCodes.UnknownMethod, Text(thenRefused), StringComparison.Ordinal);
        }

        using (var allowed = McpHarness.Full(new McpBridgeOptions { Filter = new McpMethodFilter(false, McpGlob.ParseList("windows/*"), []) }))
        {
            Assert.All(allowed.ListTools(), tool => Assert.True(tool.Name.StartsWith("windows_", StringComparison.Ordinal) || BridgeTools.Contains(tool.Name), tool.Name));
        }

        using var denied = McpHarness.Full(new McpBridgeOptions
        {
            Filter = new McpMethodFilter(false, McpGlob.ParseList("windows/*,session/*"), McpGlob.ParseList("session/quit,windows/c*")),
        });
        var names = denied.ListTools().Select(tool => tool.Name).ToArray();
        Assert.Contains("session_describe", names);
        Assert.Contains("windows_activate", names);
        Assert.DoesNotContain("session_quit", names);
        Assert.DoesNotContain("windows_close", names);
        Assert.DoesNotContain("outputs_list", names);
        Assert.Throws<McpProtocolException>(() => denied.Call("session_quit"));
        var status = denied.Call("bridge_status").StructuredContent!.Value.GetProperty("filters");
        Assert.Equal(["session/quit", "windows/c*"], status.GetProperty("deny").EnumerateArray().Select(glob => glob.GetString()));
    }

    [Fact]
    public void Window_tools_read_and_act_on_the_model()
    {
        using var harness = McpHarness.Full();
        var model = Model(harness);
        var id = model.Add("a terminal", "foot", geometry: new Box(10, 20, 300, 200));

        var listed = harness.Call("windows_list");
        Assert.NotEqual(true, listed.IsError);
        var window = Assert.Single(listed.StructuredContent!.Value.GetProperty("windows").EnumerateArray());
        Assert.Equal("foot", window.GetProperty("app_id").GetString());
        Assert.Contains("\"a terminal\"", Text(listed), StringComparison.Ordinal);

        Assert.NotEqual(true, harness.Call("windows_activate", $$"""{"id":{{id}}}""").IsError);
        Assert.Contains((id, ToplevelRequestKind.Activate), model.Requests);

        model.Refuse = true;
        var refused = harness.Call("windows_move", $$"""{"id":{{id}},"x":5,"y":5}""");
        Assert.True(refused.IsError);
        Assert.StartsWith("refused: ", Text(refused), StringComparison.Ordinal);

        var missing = harness.Call("windows_get", """{"id":424242}""");
        Assert.True(missing.IsError);
        Assert.StartsWith("not_found: ", Text(missing), StringComparison.Ordinal);

        var invalid = harness.Call("windows_get", """{"id":"nope"}""");
        Assert.True(invalid.IsError);
        Assert.StartsWith("invalid_params: ", Text(invalid), StringComparison.Ordinal);
    }

    [Fact]
    public void Capture_is_an_inline_image_scaled_to_the_bridge_default()
    {
        using var harness = McpHarness.Full(new McpBridgeOptions { MaxDimension = 64, PollInterval = TimeSpan.FromMilliseconds(50) });
        _ = MappedToplevel.Map(harness.Rig!.Host, harness.Rig.Host.Client, 60, 50, 0xFF3366AA);
        harness.Rig.Host.PumpToClient();
        harness.Rig.Host.RenderFrame();

        var scaled = harness.Call("capture_output");
        Assert.NotEqual(true, scaled.IsError);
        var image = Assert.IsType<ImageContentBlock>(scaled.Content[0]);
        Assert.Equal("image/png", image.MimeType);
        var (_, width, height) = PngCodec.Decode(image.DecodedData.ToArray());
        Assert.Equal(64, Math.Max(width, height));
        Assert.Equal((64, 48), (width, height));
        Assert.Contains("\"width\":64", Text(scaled), StringComparison.Ordinal);

        var full = harness.Call("capture_output", """{"max_dimension":0}""");
        var fullImage = Assert.IsType<ImageContentBlock>(full.Content[0]);
        Assert.Equal((160, 120), PngCodec.Decode(fullImage.DecodedData.ToArray()) is var (_, w, h) ? (w, h) : default);

        var file = Path.Combine(harness.Directory.FullName, "shot.png");
        var written = harness.Call("capture_output", $$"""{"path":"{{file}}"}""");
        Assert.NotEqual(true, written.IsError);
        Assert.DoesNotContain(written.Content, block => block is ImageContentBlock);
        Assert.Equal(file, written.StructuredContent!.Value.GetProperty("path").GetString());
        Assert.Equal((160, 120), PngCodec.Decode(File.ReadAllBytes(file)) is var (_, fw, fh) ? (fw, fh) : default);

        var region = harness.Call("capture_region", """{"x":0,"y":0,"width":40,"height":20}""");
        Assert.Equal((40, 20), PngCodec.Decode(Assert.IsType<ImageContentBlock>(region.Content[0]).DecodedData.ToArray()) is var (_, rw, rh) ? (rw, rh) : default);

        var relative = harness.Call("capture_output", """{"path":"shot.png"}""");
        Assert.True(relative.IsError);
        Assert.StartsWith("invalid_params: ", Text(relative), StringComparison.Ordinal);
    }

    [Fact]
    public void Default_max_dimension_never_scales_a_small_output_up()
    {
        using var harness = McpHarness.Full();
        var shot = harness.Call("capture_output");
        var image = Assert.IsType<ImageContentBlock>(shot.Content[0]);
        Assert.Equal((160, 120), PngCodec.Decode(image.DecodedData.ToArray()) is var (_, w, h) ? (w, h) : default);
    }

    [Fact]
    public void Descriptors_in_a_reply_are_closed_and_counted()
    {
        using var harness = McpHarness.Full(register: server =>
            server.Methods.Register("test/fd", (ref IpcParams _, IpcReply reply) =>
            {
                var handle = File.OpenHandle("/dev/null");
                var fd = (int)handle.DangerousGetHandle();
                handle.SetHandleAsInvalid();
                reply.Result.WriteStartObject();
                reply.Result.WriteNumber("fd"u8, reply.AttachFd(fd));
                reply.Result.WriteEndObject();
            }));
        var result = harness.Call("test_fd");
        Assert.NotEqual(true, result.IsError);
        Assert.EndsWith("1 descriptor was dropped: the bridge cannot pass descriptors", Text(result), StringComparison.Ordinal);
        Assert.Equal(0, result.StructuredContent!.Value.GetProperty("fd").GetInt32());
    }

    [Fact]
    public void Clipboard_read_returns_the_offered_text()
    {
        var store = new TestSelectionStore();
        using var harness = McpHarness.Full(selection: store);
        store.Offer(SelectionKind.Clipboard, ("text/plain;charset=utf-8", "from the clipboard"u8.ToArray()));
        store.Offer(SelectionKind.Primary, ("UTF8_STRING", "from primary"u8.ToArray()));

        var clipboard = harness.Call("clipboard_read");
        Assert.Equal("from the clipboard", clipboard.StructuredContent!.Value.GetProperty("text").GetString());
        var primary = harness.Call("clipboard_read", """{"kind":"primary"}""");
        Assert.Equal("from primary", primary.StructuredContent!.Value.GetProperty("text").GetString());

        store.Offer(SelectionKind.Clipboard, ("text/plain", null));
        var silent = harness.Call("clipboard_read", """{"timeout_ms":50}""");
        Assert.True(silent.IsError);
        Assert.StartsWith("failed: ", Text(silent), StringComparison.Ordinal);
    }

    [Fact]
    public void Wait_event_runs_then_after_the_subscription_and_catches_its_event()
    {
        using var harness = McpHarness.Full(register: RegisterAddWindow);
        var caught = harness.Call(
            "wait_event",
            """{"events":["window/added"],"match":{"app_id":"late"},"then":{"method":"test/add-window","params":{"app_id":"late","count":1}},"timeout_ms":5000}""");
        Assert.NotEqual(true, caught.IsError);
        var events = caught.StructuredContent!.Value.GetProperty("events");
        var first = Assert.Single(events.EnumerateArray());
        Assert.Equal("window/added", first.GetProperty("event").GetString());
        Assert.Equal("late", first.GetProperty("data").GetProperty("app_id").GetString());
        Assert.Equal(1, caught.StructuredContent.Value.GetProperty("then").GetProperty("added").GetInt32());

        var missed = harness.Call("wait_event", """{"events":["window/added"],"match":{"app_id":"late"},"timeout_ms":100}""");
        Assert.True(missed.IsError);
        Assert.StartsWith("timeout: ", Text(missed), StringComparison.Ordinal);

        var collected = harness.Call(
            "wait_event",
            """{"events":["window/added"],"match":{"app_id":"*bulk*"},"then":{"method":"test_add-window","params":{"app_id":"a-bulk-one","count":3}},"collect_ms":300}""");
        Assert.Equal(3, collected.StructuredContent!.Value.GetProperty("events").GetArrayLength());

        var failed = harness.Call("wait_event", """{"events":["window/added"],"then":{"method":"windows/get","params":{"id":999}},"timeout_ms":5000}""");
        Assert.True(failed.IsError);
        Assert.StartsWith("not_found: ", Text(failed), StringComparison.Ordinal);

        var unknown = harness.Call("wait_event", """{"events":["window/nope"],"timeout_ms":100}""");
        Assert.True(unknown.IsError);
        Assert.StartsWith("invalid_params: ", Text(unknown), StringComparison.Ordinal);

        var tool = Assert.Single(harness.ListTools(), tool => tool.Name == "wait_event");
        Assert.Contains("window/added", tool.Description, StringComparison.Ordinal);
        harness.PumpUntil(() => harness.Rig!.Server.ConnectionCount == 1);
        Assert.Equal(1, harness.Rig!.Server.ConnectionCount);
    }

    [Fact]
    public void Wait_event_spawns_through_then()
    {
        using var harness = McpHarness.Full();
        var spawned = harness.Call(
            "wait_event",
            """{"events":["window/added"],"then":{"method":"process/spawn","params":{"argv":["true"]}},"timeout_ms":100}""");
        Assert.True(spawned.IsError);
        Assert.Contains("\"pid\":", Text(spawned), StringComparison.Ordinal);
    }

    [Fact]
    public void Ids_above_two_to_the_53_come_back_as_strings_and_go_in_as_strings()
    {
        using var harness = McpHarness.Full(register: RegisterAddWindow);
        var model = Model(harness);
        model.IdBase = 1UL << 56;
        var id = model.Add("big", "big.app");

        var listed = harness.Call("windows_list");
        var window = Assert.Single(listed.StructuredContent!.Value.GetProperty("windows").EnumerateArray());
        Assert.Equal(JsonValueKind.String, window.GetProperty("id").ValueKind);
        Assert.Equal(id.ToString(System.Globalization.CultureInfo.InvariantCulture), window.GetProperty("id").GetString());
        Assert.Contains($"\"id\":\"{id}\"", Text(listed), StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Number, window.GetProperty("geometry").GetProperty("width").ValueKind);

        var schema = Assert.Single(harness.ListTools(), tool => tool.Name == "windows_get").InputSchema.GetProperty("properties").GetProperty("id");
        Assert.Equal("string", schema.GetProperty("type").GetString());
        Assert.False(schema.TryGetProperty("anyOf", out _));

        var got = harness.Call("windows_get", $$"""{"id":"{{id}}"}""");
        Assert.NotEqual(true, got.IsError);
        Assert.Equal("big", got.StructuredContent!.Value.GetProperty("title").GetString());

        var caught = harness.Call(
            "wait_event",
            """{"events":["window/added"],"match":{"app_id":"late"},"then":{"method":"test/add-window","params":{"app_id":"late","count":1}}}""");
        var added = Assert.Single(caught.StructuredContent!.Value.GetProperty("events").EnumerateArray());
        var addedId = added.GetProperty("data").GetProperty("id").GetString()!;
        Assert.Equal(id + 1, ulong.Parse(addedId, System.Globalization.CultureInfo.InvariantCulture));

        var byId = harness.Call(
            "wait_event",
            $$$"""{"events":["window/changed"],"match":{"id":"{{{id}}}"},"then":{"method":"windows/set-state","params":{"id":"{{{id}}}","maximized":true}},"timeout_ms":300}""");
        Assert.True(byId.IsError);
        Assert.Contains((id, ToplevelRequestKind.Maximize), model.Requests);
    }

    private static void RegisterAddWindow(IpcServer server) =>
        server.Methods.Register("test/add-window", (ref IpcParams parameters, IpcReply reply) =>
        {
            var appId = parameters.GetString("app_id");
            var count = parameters.GetInt("count");
            if (parameters.Failed)
            {
                return;
            }

            var model = (TestToplevelModel)server.Services.Require<IToplevelModel>();
            for (var i = 0; i < count; i++)
            {
                _ = model.Add($"window {i}", appId);
            }

            reply.Result.WriteStartObject();
            reply.Result.WriteNumber("added"u8, count);
            reply.Result.WriteEndObject();
        });
}
