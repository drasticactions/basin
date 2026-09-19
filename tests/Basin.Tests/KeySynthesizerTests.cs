using Basin.Diagnostics;
using Basin.Seat;
using Xkb;
using Xunit;

namespace Basin.Tests;

public sealed class KeySynthesizerTests
{
    private const uint LeftShift = 42;

    private sealed class RecordingSink : IKeySink
    {
        public List<(uint Key, bool Pressed)> Keys { get; } = [];

        public void NotifyKey(uint timeMs, uint key, bool pressed) => Keys.Add((key, pressed));
    }

    private sealed class CapturingLog : IBasinLogSink, IDisposable
    {
        private readonly IBasinLogSink? _previous = BasinLog.Sink;

        public CapturingLog() => BasinLog.Sink = this;

        public List<(BasinLogLevel Level, string Category, string Message)> Lines { get; } = [];

        public void Write(BasinLogLevel level, string category, ReadOnlySpan<char> message) =>
            Lines.Add((level, category, message.ToString()));

        public void Dispose() => BasinLog.Sink = _previous;
    }

    private sealed class Fixture : IDisposable
    {
        private readonly XkbKeymapSource _source = new();

        public Fixture()
        {
            Assert.True(_source.TryCompile(HostKeyboardLayout.FallbackKeymapText, out var keymap));
            keymap.Dispose();
            Synthesizer = new KeySynthesizer(Sink) { Keymap = _source.LastCompiled };
        }

        public RecordingSink Sink { get; } = new();

        public KeySynthesizer Synthesizer { get; }

        public void Dispose() => _source.Dispose();
    }

    [Fact]
    public void Plain_letters_are_one_press_and_release_each_with_no_modifier()
    {
        using var fixture = new Fixture();
        Assert.Equal(5, fixture.Synthesizer.Type("hello", 1));
        Assert.Equal(
            [(35u, true), (35u, false), (18u, true), (18u, false), (38u, true), (38u, false),
             (38u, true), (38u, false), (24u, true), (24u, false)],
            fixture.Sink.Keys);
    }

    [Fact]
    public void A_capital_holds_shift_around_the_letter()
    {
        using var fixture = new Fixture();
        Assert.Equal(1, fixture.Synthesizer.Type("H", 1));
        Assert.Equal(
            [(LeftShift, true), (35u, true), (35u, false), (LeftShift, false)],
            fixture.Sink.Keys);
    }

    [Fact]
    public void At_is_shift_and_two_on_the_us_layout()
    {
        using var fixture = new Fixture();
        Assert.Equal(1, fixture.Synthesizer.Type("@", 1));
        Assert.Equal(
            [(LeftShift, true), (3u, true), (3u, false), (LeftShift, false)],
            fixture.Sink.Keys);
    }

    [Fact]
    public void A_character_the_keymap_cannot_produce_is_logged_and_dropped_and_the_rest_still_types()
    {
        using var fixture = new Fixture();
        using var log = new CapturingLog();
        Assert.Equal(2, fixture.Synthesizer.Type("aあb", 1));
        Assert.Equal([(30u, true), (30u, false), (48u, true), (48u, false)], fixture.Sink.Keys);
        var line = Assert.Single(log.Lines, static l => l.Category == "seat");
        Assert.Equal(BasinLogLevel.Warn, line.Level);
        Assert.Contains("U+3042", line.Message);
    }

    [Fact]
    public void The_lookup_is_cached_per_keysym_and_forgotten_on_a_keymap_change()
    {
        using var fixture = new Fixture();
        Assert.True(fixture.Synthesizer.TryResolve(XkbKeysym.FromUtf32('A').Value, out var keycode, out var modifiers));
        Assert.Equal(30u, keycode);
        Assert.NotEqual(0u, modifiers);
        Assert.True(fixture.Synthesizer.TryResolve(XkbKeysym.FromUtf32('A').Value, out var again, out _));
        Assert.Equal(keycode, again);

        fixture.Synthesizer.Keymap = null;
        Assert.False(fixture.Synthesizer.TryResolve(XkbKeysym.FromUtf32('A').Value, out _, out _));
        Assert.Equal(0, fixture.Synthesizer.Type("A", 1));
        Assert.Empty(fixture.Sink.Keys);
    }
}
