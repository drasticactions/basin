using Basin.Audio.PipeWire;
using PipeWire.Native;
using Xunit;

namespace Basin.Tests;

[Collection(AudioDaemonCollection.Name)]
public sealed class AudioStreamStateTests(AudioDaemonFixture daemon)
{
    [Fact]
    public void A_linked_stream_reports_running_and_takes_a_mute()
    {
        daemon.SkipIfUnavailable();
        using var rig = new AudioRig(daemon);
        using var streams = new AudioStreams(rig.Session);
        var wav = Path.Combine(rig.Directory, "tone.wav");
        var (left, right) = AudioSignal.Sine(440, 20);
        AudioSignal.WriteWav(wav, left, right);
        rig.Play(wav, "basin-test-running-source");
        var player = rig.Node("basin-test-running-source");
        var sink = rig.Node(AudioDaemonFixture.SinkName);
        Assert.True(rig.Session.WaitFor(() => streams.Find(player.Id) is not null, 5000), "the stream was never reported");
        Assert.False(streams.Find(player.Id)!.IsRunning);

        rig.Configure(player.Id, output: true);
        rig.Configure(sink.Id, output: false, monitor: true);
        rig.Link(player.Id, sink.Id);
        Assert.True(rig.Session.WaitFor(() => streams.Find(player.Id) is { IsRunning: true }, 5000), "the linked stream never reported running");

        using var node = rig.Session.Registry.BindNode(player.Id);
        var muted = false;
        var values = Array.Empty<float>();
        node.ParamChanged += (_, param) =>
        {
            if (param.ParamType == spa_param_type.SPA_PARAM_Props)
            {
                _ = SinkVolume.ReadProps(param.Param, ref values, ref muted);
            }
        };
        node.SubscribeParams(spa_param_type.SPA_PARAM_Props);
        Assert.True(rig.Session.Roundtrip());
        Assert.False(muted);

        Assert.True(streams.SetMuted(player.Id, true));
        Assert.True(rig.Session.WaitFor(() => muted, 5000), "the stream never reported its mute");
        Assert.True(streams.SetMuted(player.Id, false));
        Assert.True(rig.Session.WaitFor(() => !muted, 5000), "the stream never reported its unmute");
        Assert.False(streams.SetMuted(uint.MaxValue, true));
    }
}
