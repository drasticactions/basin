using System.Text.Json;
using Basin.Capabilities;
using Basin.Ipc;
using Xunit;

namespace Basin.Tests;

public sealed class IpcInputTests
{
    private static JsonElement Parse(string frame) => JsonDocument.Parse(frame).RootElement.Clone();

    private static string? ErrorCode(string frame) =>
        Parse(frame).TryGetProperty("error", out var error) ? error.GetProperty("code").GetString() : null;

    [Fact]
    public void Input_group_reaches_the_compositor_path()
    {
        var input = new RecordingSynthetic();
        using var rig = new IpcTestRig(services => services.Use<IKeymapLookup>(new TableLookup()));
        rig.Server.SyntheticInput = input;
        var peer = rig.Connect();

        Assert.Null(ErrorCode(peer.Call("""{"method":"input/pointer-move","params":{"x":12.5,"y":7}}""")));
        Assert.Null(ErrorCode(peer.Call("""{"method":"input/pointer-button","params":{"button":"left"}}""")));
        Assert.Null(ErrorCode(peer.Call("""{"method":"input/pointer-button","params":{"button":273,"pressed":true}}""")));
        Assert.Null(ErrorCode(peer.Call("""{"method":"input/key","params":{"code":30}}""")));
        Assert.Null(ErrorCode(peer.Call("""{"method":"input/axis","params":{"value":-15,"axis":"horizontal"}}""")));
        Assert.Equal(["move 12.5 7", "button 272 True", "button 272 False", "button 273 True", "key 30 True", "key 30 False", "axis 1 -15 0"], input.Log);

        Assert.Equal(IpcErrorCodes.Refused, ErrorCode(peer.Call("""{"method":"input/touch","params":{"kind":"down","id":1,"x":1,"y":1}}""")));
        Assert.Equal(IpcErrorCodes.InvalidParams, ErrorCode(peer.Call("""{"method":"input/pointer-button","params":{"button":"fourth"}}""")));

        var outputs = Parse(peer.Call("""{"method":"session/describe"}"""));
        Assert.True(outputs.TryGetProperty("result", out _));
    }

    [Fact]
    public void Window_relative_input_uses_the_stack_order_when_the_stack_cannot_hit_test()
    {
        using var rig = IpcFullRig.Create();
        var model = (TestToplevelModel)rig.Services.Require<IToplevelModel>();
        var under = model.Add("under", "app.under", geometry: new Box(0, 0, 100, 80));
        var over = model.Add("over", "app.over", geometry: new Box(50, 40, 100, 80));
        ((TestToplevelStack)rig.Services.Require<IToplevelStack>()).SetOrder(under, over);
        var input = (RecordingSynthetic)rig.Server.SyntheticInput!;
        var peer = rig.Connect();

        Assert.Null(ErrorCode(peer.Call($$$"""{"method":"input/pointer-move","params":{"window":{{{under}}},"x":10,"y":10}}""")));
        Assert.Equal(["move 10 10"], input.Log);
        Assert.Equal(IpcErrorCodes.Refused, ErrorCode(peer.Call($$$"""{"method":"input/axis","params":{"value":5,"window":{{{under}}},"x":60,"y":50}}""")));
        Assert.Null(ErrorCode(peer.Call($$$"""{"method":"input/pointer-button","params":{"button":"left","window":{{{over}}},"x":1,"y":1}}""")));
        Assert.Equal(["move 10 10", "move 51 41", "button 272 True", "button 272 False"], input.Log);
        Assert.Equal(IpcErrorCodes.InvalidParams, ErrorCode(peer.Call("""{"method":"input/pointer-button","params":{"button":"left","x":1}}""")));
    }

    [Fact]
    public void Chord_presses_in_order_and_releases_in_reverse()
    {
        var input = new RecordingSynthetic();
        using var rig = new IpcTestRig(services => services.Use<IKeymapLookup>(new TableLookup()));
        rig.Server.SyntheticInput = input;
        var peer = rig.Connect();

        var reply = Parse(peer.Call("""{"method":"input/chord","params":{"chord":"Super+Shift+c"}}"""));
        Assert.Equal([42u, 125u, 46u], reply.GetProperty("result").GetProperty("codes").EnumerateArray().Select(e => e.GetUInt32()).ToArray());
        Assert.Equal(["key 42 True", "key 125 True", "key 46 True", "key 46 False", "key 125 False", "key 42 False"], input.Log);

        input.Log.Clear();
        Assert.Null(ErrorCode(peer.Call("""{"method":"input/chord","params":{"chord":"Super"}}""")));
        Assert.Equal(["key 125 True", "key 125 False"], input.Log);

        input.Log.Clear();
        Assert.Equal(IpcErrorCodes.InvalidParams, ErrorCode(peer.Call("""{"method":"input/chord","params":{"chord":"Hyper+c"}}""")));
        Assert.Empty(input.Log);
    }

    [Fact]
    public void Text_types_with_shift_and_refuses_what_the_keymap_cannot_produce()
    {
        var input = new RecordingSynthetic();
        using var rig = new IpcTestRig(services => services.Use<IKeymapLookup>(new TableLookup()));
        rig.Server.SyntheticInput = input;
        var peer = rig.Connect();

        Assert.Null(ErrorCode(peer.Call("""{"method":"input/text","params":{"text":"aC"}}""")));
        Assert.Equal(["key 30 True", "key 30 False", "key 42 True", "key 46 True", "key 46 False", "key 42 False"], input.Log);

        input.Log.Clear();
        Assert.Equal(IpcErrorCodes.InvalidParams, ErrorCode(peer.Call("""{"method":"input/text","params":{"text":"aé"}}""")));
        Assert.Empty(input.Log);
    }

    [Fact]
    public void Seat_group_goes_to_the_sink_with_one_keyboard_per_connection()
    {
        var sink = new RecordingInputSink();
        using var rig = new IpcTestRig((services, host) =>
        {
            services.Use<IInputSink>(sink);
            services.Use(host.Layout);
        });
        var peer = rig.Connect();

        Assert.Null(ErrorCode(peer.Call("""{"method":"seat/pointer-move","params":{"x":80,"y":60}}""")));
        Assert.Equal((80.0, 60.0, 160.0, 120.0), (sink.AbsoluteMotions[0].X, sink.AbsoluteMotions[0].Y, sink.AbsoluteMotions[0].Width, sink.AbsoluteMotions[0].Height));
        Assert.Null(ErrorCode(peer.Call("""{"method":"seat/key","params":{"code":30}}""")));
        Assert.Null(ErrorCode(peer.Call("""{"method":"seat/key","params":{"code":31}}""")));
        Assert.Equal(1, sink.CreatedKeyboards);
        Assert.Equal(4, sink.Keys.Count);
        Assert.DoesNotContain(IpcMethodNames.SeatChord, rig.Server.Methods.Names);
        Assert.DoesNotContain(IpcMethodNames.InputKey, rig.Server.Methods.Names);

        var second = rig.Connect();
        Assert.Null(ErrorCode(second.Call("""{"method":"seat/key","params":{"code":30}}""")));
        Assert.Equal(2, sink.CreatedKeyboards);
    }

    internal sealed class RecordingSynthetic : ISyntheticInput
    {
        public List<string> Log { get; } = [];

        public bool PointerMotionAbsolute(uint timeMs, double x, double y)
        {
            Log.Add(FormattableString.Invariant($"move {x} {y}"));
            return true;
        }

        public bool PointerButton(uint timeMs, uint button, bool pressed)
        {
            Log.Add($"button {button} {pressed}");
            return true;
        }

        public bool PointerAxis(uint timeMs, uint axis, double value, uint source)
        {
            Log.Add(FormattableString.Invariant($"axis {axis} {value} {source}"));
            return true;
        }

        public bool Key(uint timeMs, uint keycode, bool pressed)
        {
            Log.Add($"key {keycode} {pressed}");
            return true;
        }
    }

    internal sealed class TableLookup : IKeymapLookup
    {
        private static readonly Dictionary<uint, (uint Code, uint Mask)> Table = new()
        {
            [0xffeb] = (125, 0),
            [0xffe1] = (42, 0),
            [0x63] = (46, 0),
            [0x43] = (46, 1),
            [0x61] = (30, 0),
            [0x41] = (30, 1),
        };

        public bool TryKeycodeForKeysym(uint keysym, out uint keycode, out uint modifiers)
        {
            if (Table.TryGetValue(keysym, out var entry))
            {
                (keycode, modifiers) = entry;
                return true;
            }

            keycode = 0;
            modifiers = 0;
            return false;
        }

        public uint KeysymForKeycode(uint keycode) => 0;
    }
}
