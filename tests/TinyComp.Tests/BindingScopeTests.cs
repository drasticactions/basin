using Xunit;

namespace TinyComp.Tests;

public sealed class BindingScopeTests
{
    private const string Config =
        "[frame]\nstyle = \"flat\"\n\n[overview]\nhot_corner = \"none\"\n\n[bindings]\n\"Alt+b\" = { action = \"bell\", when = \"windowed\" }\n";

    private static void Chord(QuillFrameTests.QuillSession session)
    {
        session.Send("key 56 1");
        session.Send("key 48 1");
        session.Send("key 48 0");
        session.Send("key 56 0");
        Thread.Sleep(600);
    }

    [Fact]
    public void A_windowed_binding_runs_over_a_window_and_reaches_a_fullscreen_client()
    {
        using (var windowed = QuillFrameTests.QuillSession.Start("pixman", Config, ["server"]))
        {
            Assert.SkipWhen(windowed is null, "tinycomp or ssdwin is not available beside the tests");
            Chord(windowed!);
            Assert.False(windowed!.ClientPrinted("KEY 48 1"), "the windowed binding should have taken the key");
            windowed.Send("key 30 1");
            windowed.Send("key 30 0");
            Thread.Sleep(600);
            Assert.True(windowed.ClientPrinted("KEY 30 1"), "an unbound key should reach the focused client");
        }

        using var fullscreen = QuillFrameTests.QuillSession.Start("pixman", Config, ["server", "fullscreen"]);
        Assert.NotNull(fullscreen);
        Chord(fullscreen!);
        Assert.True(fullscreen!.ClientPrinted("KEY 48 1"), "a fullscreen client should have received the key");
    }
}
