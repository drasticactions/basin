using System.Reflection;
using System.Text;
using System.Text.Json;
using Basin.Ipc;
using Xunit;

namespace Basin.Tests;

public sealed class IpcMetadataTests
{
    private static JsonElement Parse(string frame) => JsonDocument.Parse(frame).RootElement.Clone();

    private static IEnumerable<string> LibraryNames() =>
        typeof(IpcMethodNames)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!);

    [Fact]
    public void Every_library_method_has_a_description_and_an_object_schema()
    {
        foreach (var name in LibraryNames())
        {
            var info = IpcSchemas.Of(name);
            Assert.False(string.IsNullOrWhiteSpace(info.Description), name);
            Assert.NotNull(info.ParamsSchema);
            using var schema = JsonDocument.Parse(info.ParamsSchema);
            Assert.Equal("object", schema.RootElement.GetProperty("type").GetString());
            Assert.Equal(JsonValueKind.Object, schema.RootElement.GetProperty("properties").ValueKind);
        }

        Assert.Equal(LibraryNames().Order(StringComparer.Ordinal), IpcSchemas.Names.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Traits_follow_the_table()
    {
        string[] destructive = ["session/quit", "windows/close", "outputs/apply", "outputs/power", "workspaces/remove"];
        string[] idempotent =
        [
            "windows/activate", "windows/set-state", "windows/move", "windows/resize", "windows/send-to-output",
            "windows/send-to-workspace", "workspaces/activate", "workspaces/deactivate", "idle/uninhibit",
        ];
        string[] readOnly =
        [
            "ipc/version", "ipc/methods", "ipc/events", "session/describe", "outputs/list", "outputs/test", "windows/list",
            "windows/get", "windows/stack", "windows/wait", "workspaces/list", "idle/status", "lock/status", "keyboard/keymap",
            "capture/output", "capture/window", "capture/region", "clipboard/read",
        ];
        foreach (var name in LibraryNames())
        {
            var traits = IpcSchemas.Of(name).Traits;
            Assert.Equal(readOnly.Contains(name), (traits & IpcMethodTraits.ReadOnly) != 0);
            Assert.Equal(destructive.Contains(name), (traits & IpcMethodTraits.Destructive) != 0);
            Assert.Equal(idempotent.Contains(name), (traits & IpcMethodTraits.Idempotent) != 0);
        }
    }

    [Fact]
    public void A_descriptor_property_is_marked()
    {
        foreach (var name in new[] { IpcMethodNames.CaptureOutput, IpcMethodNames.CaptureWindow, IpcMethodNames.CaptureRegion })
        {
            using var schema = JsonDocument.Parse(IpcSchemas.Of(name).ParamsSchema!);
            Assert.True(schema.RootElement.GetProperty("properties").GetProperty("to").GetProperty("x-basin-fd").GetBoolean());
        }

        using var keymap = JsonDocument.Parse(IpcSchemas.Of(IpcMethodNames.KeyboardKeymap).ParamsSchema!);
        Assert.True(keymap.RootElement.GetProperty("properties").GetProperty("fd").GetProperty("x-basin-fd").GetBoolean());
    }

    [Fact]
    public void A_call_from_only_the_required_keys_is_never_invalid_params()
    {
        using var rig = IpcFullRig.Create();
        var peer = rig.Connect();
        var registered = rig.Server.Methods.Names;
        Assert.Equal(LibraryNames().Order(StringComparer.Ordinal), registered.Order(StringComparer.Ordinal));

        foreach (var name in registered)
        {
            Assert.True(rig.Server.Methods.TryGetInfo(name, out var info), name);
            using var schema = JsonDocument.Parse(info.ParamsSchema!);
            var parameters = RequiredCall(schema.RootElement);
            var reply = Parse(peer.Call($$"""{"method":"{{name}}","params":{{parameters}}}"""));
            if (reply.TryGetProperty("error", out var error))
            {
                var code = error.GetProperty("code").GetString();
                Assert.True(code is not (IpcErrorCodes.InvalidParams or IpcErrorCodes.Internal), $"{name} {parameters}: {error}");
            }
        }

        foreach (var fd in peer.ReceivedFds)
        {
            _ = UnixSocket.Close(fd);
        }
    }

    [Fact]
    public void Methods_answers_names_or_detail()
    {
        using var rig = new IpcTestRig();
        rig.Server.Methods.Register("test/plain", (ref IpcParams _, IpcReply reply) => IpcWrite.Empty(reply), line: "plain {word}");
        rig.Server.Methods.Register(
            "test/described",
            (ref IpcParams _, IpcReply reply) => IpcWrite.Empty(reply),
            info: new IpcMethodInfo("Say it.", """{ "type": "object", "properties": { "n": { "type": "integer" } } }""", IpcMethodTraits.Destructive));
        var peer = rig.Connect();

        var names = Parse(peer.Call("""{"method":"ipc/methods"}""")).GetProperty("result").GetProperty("methods");
        Assert.All(names.EnumerateArray(), name => Assert.Equal(JsonValueKind.String, name.ValueKind));
        Assert.Contains("test/plain", names.EnumerateArray().Select(name => name.GetString()));

        var detail = Parse(peer.Call("""{"method":"ipc/methods","params":{"detail":true}}""")).GetProperty("result").GetProperty("methods");
        var byName = detail.EnumerateArray().ToDictionary(method => method.GetProperty("name").GetString()!);
        Assert.Equal(names.GetArrayLength(), byName.Count);

        var plain = byName["test/plain"];
        Assert.Equal(JsonValueKind.Null, plain.GetProperty("description").ValueKind);
        Assert.Equal(JsonValueKind.Null, plain.GetProperty("schema").ValueKind);
        Assert.Equal("plain {word}", plain.GetProperty("line").GetString());
        Assert.False(plain.GetProperty("read_only").GetBoolean());

        var described = byName["test/described"];
        Assert.Equal("Say it.", described.GetProperty("description").GetString());
        Assert.Equal("""{"type":"object","properties":{"n":{"type":"integer"}}}""", described.GetProperty("schema").GetRawText());
        Assert.True(described.GetProperty("destructive").GetBoolean());
        Assert.Equal(JsonValueKind.Null, described.GetProperty("line").ValueKind);

        var version = byName[IpcMethodNames.Version];
        Assert.True(version.GetProperty("read_only").GetBoolean());
        Assert.Equal(JsonValueKind.Object, version.GetProperty("schema").ValueKind);
    }

    [Fact]
    public void Register_refuses_a_schema_that_is_not_an_object_schema()
    {
        using var rig = new IpcTestRig();
        var methods = rig.Server.Methods;
        IpcHandler handler = (ref IpcParams _, IpcReply reply) => IpcWrite.Empty(reply);
        Assert.Throws<ArgumentException>(() => methods.Register("test/a", handler, info: new IpcMethodInfo("a", "{", IpcMethodTraits.None)));
        Assert.Throws<ArgumentException>(() => methods.Register("test/b", handler, info: new IpcMethodInfo("b", """{"type":"array"}""", IpcMethodTraits.None)));
        Assert.Throws<ArgumentException>(() => methods.Register("test/c", handler, info: new IpcMethodInfo("c", "[]", IpcMethodTraits.None)));
        Assert.False(methods.Contains("test/b"));
        methods.Register("test/d", handler, info: new IpcMethodInfo("d", null, IpcMethodTraits.ReadOnly));
        Assert.True(methods.TryGetInfo("test/d", out var info));
        Assert.Null(info.ParamsSchema);
    }

    internal static string RequiredCall(JsonElement schema)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (schema.TryGetProperty("required", out var required))
        {
            var properties = schema.GetProperty("properties");
            foreach (var name in required.EnumerateArray().Select(item => item.GetString()!))
            {
                values[name] = Value(properties.GetProperty(name));
            }
        }

        if (schema.TryGetProperty("examples", out var examples) && examples[0].ValueKind == JsonValueKind.Object)
        {
            foreach (var property in examples[0].EnumerateObject())
            {
                values[property.Name] = property.Value.GetRawText();
            }
        }

        var builder = new StringBuilder("{");
        foreach (var (name, value) in values)
        {
            _ = builder.Append(builder.Length > 1 ? "," : string.Empty).Append('"').Append(name).Append("\":").Append(value);
        }

        return builder.Append('}').ToString();
    }

    private static string Value(JsonElement property)
    {
        if (property.TryGetProperty("examples", out var examples))
        {
            return examples[0].GetRawText();
        }

        if (property.TryGetProperty("enum", out var choices))
        {
            return choices[0].GetRawText();
        }

        if (property.TryGetProperty("anyOf", out var any))
        {
            return Value(any[0]);
        }

        return property.GetProperty("type").GetString() switch
        {
            "integer" => property.TryGetProperty("minimum", out var minimum) ? minimum.GetRawText() : "0",
            "number" => property.TryGetProperty("exclusiveMinimum", out _) ? "1" : "0",
            "boolean" => "false",
            "string" => "\"\"",
            "array" => property.TryGetProperty("minItems", out _) ? $"[{Value(property.GetProperty("items"))}]" : "[]",
            "object" => RequiredCall(property),
            var other => throw new InvalidOperationException($"no value for type {other}"),
        };
    }
}
