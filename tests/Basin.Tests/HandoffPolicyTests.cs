using Basin.Config;
using Basin.Diagnostics;
using Basin.Host;
using Xunit;

namespace Basin.Tests;

public sealed class HandoffPolicyTests
{
    [Theory]
    [InlineData(HandoffMode.Auto, false, false, HandoffStop.Orderly, false)]
    [InlineData(HandoffMode.Auto, false, false, HandoffStop.SessionStart, false)]
    [InlineData(HandoffMode.Auto, true, false, HandoffStop.SessionStart, false)]
    [InlineData(HandoffMode.Auto, true, true, HandoffStop.SessionStart, true)]
    [InlineData(HandoffMode.Auto, true, true, HandoffStop.Orderly, false)]
    [InlineData(HandoffMode.Auto, true, true, HandoffStop.Failure, false)]
    [InlineData(HandoffMode.Retain, false, false, HandoffStop.Orderly, true)]
    [InlineData(HandoffMode.Retain, true, false, HandoffStop.SessionStart, true)]
    [InlineData(HandoffMode.Retain, false, false, HandoffStop.Failure, false)]
    [InlineData(HandoffMode.Off, true, true, HandoffStop.SessionStart, false)]
    [InlineData(HandoffMode.Off, false, false, HandoffStop.Orderly, false)]
    public void The_policy_retains_only_where_the_table_says(
        HandoffMode mode, bool greeter, bool greetd, HandoffStop stop, bool retains)
    {
        Assert.Equal(retains, new HandoffPolicy(mode, greeter, greetd).RetainsOn(stop));
    }

    [Fact]
    public void Off_turns_inheriting_off_and_keeps_the_greeter_from_stamping_the_session()
    {
        var off = new HandoffPolicy(HandoffMode.Off, greeter: true, greetd: true);
        Assert.False(off.Inherits);
        Assert.False(off.StampsSession);
        Assert.True(new HandoffPolicy(HandoffMode.Auto, greeter: true).StampsSession);
        Assert.False(new HandoffPolicy(HandoffMode.Auto).StampsSession);
    }

    [Theory]
    [InlineData(null, HandoffMode.Auto, null, HandoffMode.Auto)]
    [InlineData(null, HandoffMode.Auto, HandoffMode.Retain, HandoffMode.Retain)]
    [InlineData(null, HandoffMode.Off, HandoffMode.Retain, HandoffMode.Off)]
    [InlineData(HandoffMode.Retain, HandoffMode.Off, null, HandoffMode.Retain)]
    [InlineData(HandoffMode.Off, HandoffMode.Auto, HandoffMode.Retain, HandoffMode.Off)]
    [InlineData(HandoffMode.Auto, HandoffMode.Retain, null, HandoffMode.Auto)]
    public void The_flag_beats_the_key_and_the_key_beats_the_environment(
        HandoffMode? flag, HandoffMode key, HandoffMode? environment, HandoffMode resolved)
    {
        Assert.Equal(resolved, new HandoffChoice(flag, environment).Resolve(key));
    }

    [Fact]
    public void The_environment_is_read_once_and_removed()
    {
        var saved = Environment.GetEnvironmentVariable(HandoffChoice.Variable);
        try
        {
            Environment.SetEnvironmentVariable(HandoffChoice.Variable, "retain");
            var choice = HandoffChoice.Take(null);
            Assert.Equal(HandoffMode.Retain, choice.Environment);
            Assert.Null(Environment.GetEnvironmentVariable(HandoffChoice.Variable));
            Assert.Null(HandoffChoice.Take(null).Environment);
        }
        finally
        {
            Environment.SetEnvironmentVariable(HandoffChoice.Variable, saved);
        }
    }

    [Fact]
    public void The_handoff_table_reads_every_key_and_defaults_the_rest()
    {
        var table = Tomlyn.Toml.ToModel("""
            [handoff]
            mode = "retain"
            transition = "cut"
            duration_ms = 250
            """);
        var settings = HandoffSettings.Read(new TomlReader(table, default).Section("handoff"));
        Assert.Equal(HandoffMode.Retain, settings.Mode);
        Assert.Equal(HandoffTransitionKind.Cut, settings.Transition);
        Assert.Equal(250, settings.DurationMs);
        Assert.Equal(3000, settings.HoldLimitMs);

        var none = HandoffSettings.Read(null);
        Assert.Equal(HandoffMode.Auto, none.Mode);
        Assert.Equal(HandoffTransitionKind.Crossfade, none.Transition);
        Assert.Equal(400, none.DurationMs);
    }

    [Fact]
    public void The_lines_name_the_output_and_the_outcome()
    {
        Assert.Equal(
            "HANDOFF inherit DP-2 mode=3840x2160@59.98 modeset=no reason=matched",
            HandoffLines.Inherit(new HandoffInheritance("DP-2", new OutputMode(3840, 2160, 59_981), false, HandoffReason.Matched, 0)));
        Assert.Equal(
            "HANDOFF inherit DP-2 mode=1920x1080@60 modeset=yes reason=refused errno=22",
            HandoffLines.Inherit(new HandoffInheritance("DP-2", new OutputMode(1920, 1080, 60_000), true, HandoffReason.Refused, 22)));
        Assert.Equal("HANDOFF retain DP-2 fb=461", HandoffLines.Retain(new HandoffRetention("DP-2", 461, null)));
        Assert.Equal("HANDOFF refused DP-2 closefb errno=25", HandoffLines.Retain(new HandoffRetention("DP-2", 461, "closefb errno=25")));
    }
}
