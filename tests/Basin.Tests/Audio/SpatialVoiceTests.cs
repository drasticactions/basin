using Basin.Audio.PipeWire;
using Basin.Diagnostics;
using Xunit;

namespace Basin.Tests;

[Collection(AudioDaemonCollection.Name)]
public sealed class SpatialVoiceTests(AudioDaemonFixture daemon)
{
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan Take = TimeSpan.FromMilliseconds(1200);

    private const string DefaultSofa = "/usr/share/libmysofa/default.sofa";

    [Fact]
    public void A_voice_attaches_a_stream_through_target_object_and_leaves_nothing_behind()
    {
        daemon.SkipIfUnavailable();
        Lifecycle("warm");
        var fds = FdSnapshot.Take();
        var live = BasinCounters.LiveObjects;

        Lifecycle("measured");

        LeakTracking.Expect(live, BasinCounters.LiveObjects);
        Assert.Equal(fds, FdSnapshot.Take());
    }

    private void Lifecycle(string name)
    {
        using var rig = new AudioRig(daemon);
        using var streams = new AudioStreams(rig.Session);
        var wav = Path.Combine(rig.Directory, "tone.wav");
        var (left, right) = AudioSignal.Sine(440, 20);
        AudioSignal.WriteWav(wav, left, right);
        rig.Play(wav, $"basin-test-{name}-source");
        AudioStream? stream = null;
        Assert.True(rig.Session.WaitFor(() => (stream = streams.Streams.FirstOrDefault(s => s.ApplicationName == $"basin-test-{name}-source")) is not null, 5000));

        var voice = SpatialVoice.Create(rig.Session, new VoiceGraph(VoiceMode.Pan), $"basin-test-{name}", out var error);
        Assert.True(voice is not null, error);
        var captureId = voice.CaptureNodeId;
        Assert.DoesNotContain(streams.Streams, s => s.NodeId == captureId);

        string? target = null;
        rig.Session.MetadataChanged += property =>
        {
            if (property.Subject == stream!.NodeId && property.Key == "target.object")
            {
                target = property.Value;
            }
        };

        Assert.True(voice.Attach(stream!));
        Assert.True(rig.Session.WaitFor(() => target == voice.CaptureSerial.ToString(), 3000));
        Assert.Contains(stream!.NodeId, voice.Streams);

        Assert.True(voice.Detach(stream.NodeId));
        Assert.True(rig.Session.WaitFor(() => target is null, 3000));

        voice.Attach(stream);
        Assert.True(rig.Session.WaitFor(() => target is not null, 3000));
        voice.Dispose();
        Assert.True(rig.Session.WaitFor(() => target is null, 3000));
        Assert.True(rig.Session.WaitFor(() => rig.Session.FindNode($"basin-test-{name}") is null, 3000));
        Assert.Null(rig.Session.FindNode($"basin-test-{name}.output"));
    }

    [Fact]
    public void A_move_by_someone_else_releases_the_stream()
    {
        daemon.SkipIfUnavailable();
        using var rig = new AudioRig(daemon);
        using var streams = new AudioStreams(rig.Session);
        var wav = Path.Combine(rig.Directory, "tone.wav");
        var (left, right) = AudioSignal.Sine(440, 20);
        AudioSignal.WriteWav(wav, left, right);
        rig.Play(wav, "basin-test-moved");
        AudioStream? stream = null;
        Assert.True(rig.Session.WaitFor(() => (stream = streams.Streams.FirstOrDefault(s => s.ApplicationName == "basin-test-moved")) is not null, 5000));
        using var voice = SpatialVoice.Create(rig.Session, new VoiceGraph(VoiceMode.Pan), "basin-test-moved-voice", out var error);
        Assert.True(voice is not null, error);
        uint released = 0;
        voice.Released += (_, id) => released = id;
        voice.Attach(stream!);
        rig.Session.Roundtrip();

        var sink = rig.Node(AudioDaemonFixture.SinkName);
        rig.Session.DefaultMetadata!.SetProperty(stream!.NodeId, "target.object", "Spa:Id", sink.GetProperty("object.serial"));
        Assert.True(rig.Session.WaitFor(() => released == stream.NodeId, 3000));
        Assert.Empty(voice.Streams);
    }

    [Fact]
    public void Handing_a_stream_to_a_new_voice_does_not_read_the_old_voice_clearing_it_as_a_move()
    {
        daemon.SkipIfUnavailable();
        using var rig = new AudioRig(daemon);
        using var streams = new AudioStreams(rig.Session);
        var wav = Path.Combine(rig.Directory, "tone.wav");
        var (left, right) = AudioSignal.Sine(440, 20);
        AudioSignal.WriteWav(wav, left, right);
        rig.Play(wav, "basin-test-handoff");
        AudioStream? stream = null;
        Assert.True(rig.Session.WaitFor(() => (stream = streams.Streams.FirstOrDefault(s => s.ApplicationName == "basin-test-handoff")) is not null, 5000));
        using var first = SpatialVoice.Create(rig.Session, new VoiceGraph(VoiceMode.Pan), "basin-test-handoff-1", out var error);
        Assert.True(first is not null, error);
        first.Attach(stream!);
        rig.Session.Roundtrip();
        using var second = SpatialVoice.Create(rig.Session, new VoiceGraph(VoiceMode.Pan), "basin-test-handoff-2", out error);
        Assert.True(second is not null, error);
        var released = false;
        second.Released += (_, _) => released = true;

        first.Detach(stream!.NodeId);
        second.Attach(stream);
        rig.Session.Roundtrip();
        rig.Session.Roundtrip();

        Assert.False(released);
        Assert.Contains(stream.NodeId, second.Streams);
        var sink = rig.Node(AudioDaemonFixture.SinkName);
        rig.Session.DefaultMetadata!.SetProperty(stream.NodeId, "target.object", "Spa:Id", sink.GetProperty("object.serial"));
        Assert.True(rig.Session.WaitFor(() => released, 3000));
    }

    [Fact]
    public void A_headphone_sink_that_starts_running_stays_headphones()
    {
        daemon.SkipIfUnavailable();
        using var rig = new AudioRig(daemon);
        using var headphones = rig.Session.Core.CreateObject("adapter", PipeWire.PipeWireInterfaces.Node, 3,
        [
            new("factory.name", "support.null-audio-sink"),
            new("node.name", "basin-test-headphones"),
            new("media.class", "Audio/Sink"),
            new("audio.position", "[ FL FR ]"),
            new("device.form-factor", "headphones"),
        ]);
        var sink = rig.Node("basin-test-headphones");
        using var watch = new AudioOutputWatch(rig.Session);
        rig.Session.DefaultMetadata!.SetProperty(0, "default.audio.sink", "Spa:String:JSON", """{"name":"basin-test-headphones"}""");
        Assert.True(rig.Session.WaitFor(() => watch.Kind == AudioOutputKind.Headphones, 3000));
        var flips = 0;
        watch.Changed += _ => flips++;

        var wav = Path.Combine(rig.Directory, "tone.wav");
        var (left, right) = AudioSignal.Sine(440, 20);
        AudioSignal.WriteWav(wav, left, right);
        rig.Play(wav, "basin-test-headphone-source");
        var player = rig.Node("basin-test-headphone-source");
        rig.Configure(player.Id, output: true);
        rig.Configure(sink.Id, output: false);
        rig.Link(player.Id, sink.Id);
        rig.Pump(TimeSpan.FromMilliseconds(600));

        Assert.Equal(AudioOutputKind.Headphones, watch.Kind);
        Assert.Equal(0, flips);
        rig.Session.DefaultMetadata.SetProperty(0, "default.audio.sink", null, null);
        rig.Session.Roundtrip();
    }

    [Fact]
    public void Pan_places_the_sound_left_and_right()
    {
        daemon.SkipIfUnavailable();
        using var rig = new AudioRig(daemon);
        using var voice = Chain(rig, new VoiceGraph(VoiceMode.Pan), AudioSignal.Sine(1000, 30), "pan");

        voice.Update(VoiceParams.Centered with { Azimuth = 90 });
        var left = Record(rig, "pan-left");
        voice.Update(VoiceParams.Centered with { Azimuth = 270 });
        var right = Record(rig, "pan-right");

        Assert.True(AudioSignal.RmsDb(left.Left) - AudioSignal.RmsDb(left.Right) > 20, Describe(left));
        Assert.True(AudioSignal.RmsDb(right.Right) - AudioSignal.RmsDb(right.Left) > 20, Describe(right));
    }

    [Fact]
    public void Gain_drops_the_level_by_its_decibels()
    {
        daemon.SkipIfUnavailable();
        using var rig = new AudioRig(daemon);
        using var voice = Chain(rig, new VoiceGraph(VoiceMode.Pan), AudioSignal.Sine(1000, 30), "gain");

        voice.Update(VoiceParams.Centered);
        var full = Record(rig, "gain-full");
        voice.Update(VoiceParams.Centered with { GainDb = -12 });
        var quiet = Record(rig, "gain-quiet");

        var drop = AudioSignal.RmsDb(full.Left) - AudioSignal.RmsDb(quiet.Left);
        Assert.InRange(drop, 11, 13);
    }

    [Fact]
    public void A_lower_cutoff_takes_the_energy_out_above_four_kilohertz()
    {
        daemon.SkipIfUnavailable();
        using var rig = new AudioRig(daemon);
        using var voice = Chain(rig, new VoiceGraph(VoiceMode.Pan), AudioSignal.Noise(30), "cutoff");

        voice.Update(VoiceParams.Centered);
        var open = Record(rig, "cutoff-open");
        voice.Update(VoiceParams.Centered with { CutoffHz = 1000 });
        var muffled = Record(rig, "cutoff-low");

        var loss = AudioSignal.HighBandDb(open.Left, 4000) - AudioSignal.HighBandDb(muffled.Left, 4000);
        Assert.True(loss > 12, $"the band above 4 kHz fell by {loss:F1} dB");
    }

    [Fact]
    public void Hrtf_places_the_sound_left_and_right()
    {
        daemon.SkipIfUnavailable();
        Assert.SkipUnless(SofaFile.TryProbe(DefaultSofa, out var min, out var max, out var reason), reason ?? "no SOFA file");
        using var rig = new AudioRig(daemon);
        var graph = new VoiceGraph(VoiceMode.Hrtf, DefaultSofa) { HrtfRadiusMin = min, HrtfRadiusMax = max };
        using var voice = Chain(rig, graph, AudioSignal.Noise(30), "hrtf");
        voice.MinHrtfInterval = TimeSpan.Zero;

        voice.Update(VoiceParams.Centered with { Azimuth = 90 });
        var left = Record(rig, "hrtf-left");
        voice.Update(VoiceParams.Centered with { Azimuth = 270 });
        var right = Record(rig, "hrtf-right");

        Assert.True(AudioSignal.RmsDb(left.Left) - AudioSignal.RmsDb(left.Right) > 6, Describe(left));
        Assert.True(AudioSignal.RmsDb(right.Right) - AudioSignal.RmsDb(right.Left) > 6, Describe(right));
    }

    [Fact]
    public void Hrtf_changes_are_rate_limited_and_the_last_one_still_arrives()
    {
        daemon.SkipIfUnavailable();
        Assert.SkipUnless(SofaFile.TryProbe(DefaultSofa, out _, out _, out var reason), reason ?? "no SOFA file");
        using var rig = new AudioRig(daemon);
        using var voice = SpatialVoice.Create(rig.Session, new VoiceGraph(VoiceMode.Hrtf, DefaultSofa), "basin-test-hrtf-limit", out var error);
        Assert.True(voice is not null, error);
        voice.MinHrtfInterval = TimeSpan.FromMilliseconds(200);

        voice.Update(VoiceParams.Centered with { Azimuth = 10 });
        var first = voice.HrtfWrites;
        for (var azimuth = 12; azimuth <= 60; azimuth += 4)
        {
            voice.Update(VoiceParams.Centered with { Azimuth = azimuth, GainDb = -azimuth / 10f });
        }

        Assert.Equal(first, voice.HrtfWrites);
        rig.Pump(TimeSpan.FromMilliseconds(400));
        Assert.Equal(first + 1, voice.HrtfWrites);
        Assert.Equal(60, voice.Current.Azimuth);
    }

    [Theory]
    [InlineData("headphone", null, null, AudioOutputKind.Headphones)]
    [InlineData("headset", null, null, AudioOutputKind.Headphones)]
    [InlineData(null, "headphones", "analog-output-headphones", AudioOutputKind.Headphones)]
    [InlineData(null, null, "analog-output-headphones", AudioOutputKind.Headphones)]
    [InlineData(null, "speaker", "analog-output-speaker", AudioOutputKind.Speakers)]
    [InlineData(null, "hdmi", "hdmi-output-2", AudioOutputKind.Speakers)]
    [InlineData("internal", null, null, AudioOutputKind.Speakers)]
    [InlineData(null, null, null, AudioOutputKind.Unknown)]
    public void Headphones_are_told_apart_from_speakers(string? form, string? port, string? route, AudioOutputKind expected)
    {
        Assert.Equal(expected, AudioOutputWatch.Classify(form, port, route));
    }

    [Fact]
    public void The_default_sink_name_is_read_from_its_json()
    {
        Assert.Equal("alsa_output.pci-0000_c4_00.6.analog-stereo", AudioOutputWatch.SinkNameOf("""{"name":"alsa_output.pci-0000_c4_00.6.analog-stereo"}"""));
        Assert.Null(AudioOutputWatch.SinkNameOf("not json"));
        Assert.Null(AudioOutputWatch.SinkNameOf(null));
    }

    [Fact]
    public void A_sofa_file_reports_the_distances_it_covers()
    {
        Assert.SkipUnless(File.Exists(DefaultSofa), $"{DefaultSofa} is not installed");
        Assert.True(SofaFile.TryProbe(DefaultSofa, out var min, out var max, out var reason), reason);
        Assert.True(min > 0 && max >= min, $"{min}..{max}");
        Assert.False(SofaFile.TryProbe("/nonexistent.sofa", out _, out _, out reason));
        Assert.Contains("does not exist", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_move_between_two_sinks_of_one_kind_reads_unknown_until_the_new_node_reports()
    {
        daemon.SkipIfUnavailable();
        using var rig = new AudioRig(daemon);
        PipeWire.PipeWireProxy Sink(string name) => rig.Session.Core.CreateObject("adapter", PipeWire.PipeWireInterfaces.Node, 3,
        [
            new("factory.name", "support.null-audio-sink"),
            new("node.name", name),
            new("media.class", "Audio/Sink"),
            new("audio.position", "[ FL FR ]"),
            new("device.form-factor", "headphones"),
        ]);
        using var first = Sink("basin-test-headphones-a");
        using var second = Sink("basin-test-headphones-b");
        _ = rig.Node("basin-test-headphones-a");
        _ = rig.Node("basin-test-headphones-b");
        using var watch = new AudioOutputWatch(rig.Session);
        rig.Session.DefaultMetadata!.SetProperty(0, "default.audio.sink", "Spa:String:JSON", """{"name":"basin-test-headphones-a"}""");
        Assert.True(rig.Session.WaitFor(() => watch.Kind == AudioOutputKind.Headphones, 3000));

        var kinds = new List<AudioOutputKind>();
        watch.Changed += w => kinds.Add(w.Kind);
        rig.Session.DefaultMetadata.SetProperty(0, "default.audio.sink", "Spa:String:JSON", """{"name":"basin-test-headphones-b"}""");
        Assert.True(rig.Session.WaitFor(() => watch.SinkName == "basin-test-headphones-b" && watch.Kind == AudioOutputKind.Headphones, 3000));
        Assert.Equal([AudioOutputKind.Unknown, AudioOutputKind.Headphones], kinds);
        rig.Session.DefaultMetadata.SetProperty(0, "default.audio.sink", null, null);
        rig.Session.Roundtrip();
    }

    [Fact]
    public void A_voice_follows_the_default_sink_route()
    {
        daemon.SkipIfUnavailable();
        using var rig = new AudioRig(daemon);
        using var watch = new AudioOutputWatch(rig.Session);
        var changes = 0;
        watch.Changed += _ => changes++;

        var sink = rig.Node(AudioDaemonFixture.SinkName);
        rig.Session.DefaultMetadata!.SetProperty(0, "default.audio.sink", "Spa:String:JSON", $$"""{"name":"{{AudioDaemonFixture.SinkName}}"}""");

        Assert.True(rig.Session.WaitFor(() => watch.SinkName == AudioDaemonFixture.SinkName && changes > 0, 3000));
        Assert.Equal(AudioOutputKind.Speakers, watch.Kind);
        _ = sink;
        rig.Session.DefaultMetadata.SetProperty(0, "default.audio.sink", null, null);
        rig.Session.Roundtrip();
    }

    [Fact]
    public void The_wet_path_fills_the_gaps_between_bursts_with_the_room()
    {
        daemon.SkipIfUnavailable();
        using var rig = new AudioRig(daemon);
        var impulse = RoomImpulse.PathFor(0.8f, directory: rig.Directory);
        using var voice = Chain(rig, new VoiceGraph(VoiceMode.Pan, null, impulse), Bursts(30), "reverb");

        voice.Update(VoiceParams.Centered);
        var dry = Record(rig, "reverb-dry");
        voice.Update(VoiceParams.Centered with { WetDb = 0 });
        var wet = Record(rig, "reverb-wet");

        var dryGap = QuietestDb(dry.Left);
        var wetGap = QuietestDb(wet.Left);
        Assert.True(wetGap - dryGap > 20, $"the gaps read {dryGap:F1} dB dry and {wetGap:F1} dB wet");
    }

    [Fact]
    public void A_stereo_voice_keeps_the_placement_and_adds_the_room()
    {
        daemon.SkipIfUnavailable();
        using var rig = new AudioRig(daemon);
        var impulse = RoomImpulse.PathFor(0.6f, directory: rig.Directory);
        var (left, _) = AudioSignal.Noise(30);
        using var voice = Chain(rig, new VoiceGraph(VoiceMode.Stereo, null, impulse), (left, new float[left.Length]), "stereo");

        voice.Update(VoiceParams.Centered);
        var dry = Record(rig, "stereo-dry");
        voice.Update(VoiceParams.Centered with { WetDb = -6 });
        var wet = Record(rig, "stereo-wet");

        Assert.True(AudioSignal.RmsDb(dry.Left) - AudioSignal.RmsDb(dry.Right) > 40, Describe(dry));
        Assert.True(AudioSignal.RmsDb(wet.Left) - AudioSignal.RmsDb(wet.Right) > 3, Describe(wet));
        Assert.True(AudioSignal.RmsDb(wet.Right) > AudioSignal.RmsDb(dry.Right) + 30, Describe(wet));
    }

    [Fact]
    public void Quantized_updates_send_nothing_for_changes_below_the_quantum()
    {
        daemon.SkipIfUnavailable();
        using var rig = new AudioRig(daemon);
        using var voice = SpatialVoice.Create(rig.Session, new VoiceGraph(VoiceMode.Pan), "basin-test-quantum", out var error);
        Assert.True(voice is not null, error);

        voice.Update(VoiceParams.Centered with { Azimuth = 10, GainDb = -3 });
        var writes = voice.ParamWrites;
        voice.Update(VoiceParams.Centered with { Azimuth = 10.4f, GainDb = -3.1f, CutoffHz = 19800 });
        Assert.Equal(writes, voice.ParamWrites);
        voice.Update(VoiceParams.Centered with { Azimuth = 12, GainDb = -3.1f });
        Assert.Equal(writes + 1, voice.ParamWrites);
    }

    private static (float[] Left, float[] Right) Bursts(double seconds)
    {
        var (left, right) = AudioSignal.Noise(seconds);
        for (var i = 0; i < left.Length; i++)
        {
            if (i % (AudioSignal.Rate / 2) >= AudioSignal.Rate / 20)
            {
                left[i] = right[i] = 0;
            }
        }

        return (left, right);
    }

    private static double QuietestDb(float[] samples)
    {
        var window = AudioSignal.Rate / 50;
        var quietest = double.MaxValue;
        for (var start = 0; start + window <= samples.Length; start += window)
        {
            quietest = Math.Min(quietest, AudioSignal.RmsDb(samples.AsSpan(start, window)));
        }

        return quietest;
    }

    private static string Describe((float[] Left, float[] Right) take) =>
        $"left {AudioSignal.RmsDb(take.Left):F1} dB, right {AudioSignal.RmsDb(take.Right):F1} dB over {take.Left.Length} frames";

    private static SpatialVoice Chain(AudioRig rig, VoiceGraph graph, (float[] Left, float[] Right) source, string name) =>
        SpatialVoiceTestChain.Start(rig, graph, name, source);

    private static (float[] Left, float[] Right) Record(AudioRig rig, string name)
    {
        rig.Pump(Settle);
        var wav = Path.Combine(rig.Directory, $"{name}.wav");
        var recorder = rig.Record(wav, $"basin-test-{name}");
        var node = rig.Node($"basin-test-{name}");
        var sink = rig.Node(AudioDaemonFixture.SinkName);
        rig.Configure(node.Id, output: false);
        rig.Link(sink.Id, node.Id, monitor: true);
        rig.Pump(Take);
        AudioRig.Stop(recorder);
        return AudioSignal.Steady(AudioSignal.ReadWav(wav), 0.3);
    }
}
