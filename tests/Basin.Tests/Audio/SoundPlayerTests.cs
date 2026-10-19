using Basin.Audio.PipeWire;
using Basin.Diagnostics;
using Xunit;

namespace Basin.Tests;

[Collection(AudioDaemonCollection.Name)]
public sealed class SoundPlayerTests(AudioDaemonFixture daemon)
{
    [Fact]
    public void A_wav_decodes_to_mono_at_48_kilohertz()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"basin-clip-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var stereo = Path.Combine(directory, "stereo.wav");
            AudioSignal.WriteWav(stereo, [0.5f, 0.5f, 0.5f], [0.1f, 0.1f, 0.1f]);
            using (var file = File.OpenRead(stereo))
            {
                var clip = SoundClip.FromWav("stereo", file);
                Assert.Equal(3, clip.Samples.Length);
                Assert.All(clip.Samples, sample => Assert.Equal(0.3f, sample, 5));
            }

            var pcm = Path.Combine(directory, "pcm.wav");
            WritePcm16(pcm, 24000, Enumerable.Repeat((short)16384, 2400).ToArray());
            using (var file = File.OpenRead(pcm))
            {
                var clip = SoundClip.FromWav("pcm", file);
                Assert.Equal(4800, clip.Samples.Length);
                Assert.Equal(0.5f, clip.Samples[100], 4);
                Assert.Equal(TimeSpan.FromMilliseconds(100), clip.Duration);
            }

            var unsigned = Path.Combine(directory, "pcm8.wav");
            WritePcm8(unsigned, 22050, Enumerable.Repeat((byte)192, 2205).ToArray());
            using (var file = File.OpenRead(unsigned))
            {
                var clip = SoundClip.FromWav("pcm8", file);
                Assert.Equal(4800, clip.Samples.Length);
                Assert.Equal(0.5f, clip.Samples[100], 4);
            }

            using var junk = new MemoryStream("not a wav file"u8.ToArray());
            Assert.Throws<InvalidDataException>(() => SoundClip.FromWav("junk", junk));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void A_sound_placed_to_the_left_plays_louder_on_the_left()
    {
        daemon.SkipIfUnavailable();
        using var rig = new AudioRig(daemon);
        using var player = Linked(rig, "basin-test-player", out _);
        var clip = Noise(0.9);

        var take = RecordWhile(rig, "player-left", () => player.Play(clip, new SoundPlacement(0, 90)));

        Assert.True(AudioSignal.RmsDb(take.Left) - AudioSignal.RmsDb(take.Right) > 20,
            $"left {AudioSignal.RmsDb(take.Left):F1} dB, right {AudioSignal.RmsDb(take.Right):F1} dB");
        Assert.Equal(0, player.Playing);
    }

    [Fact]
    public void Playing_and_mixing_allocate_nothing()
    {
        daemon.SkipIfUnavailable();
        using var rig = new AudioRig(daemon);
        using var player = Linked(rig, "basin-test-player-alloc", out _);
        var clips = new[] { Noise(0.05), Noise(0.08, 3) };

        long Measure()
        {
            long total = 0;
            for (var i = 0; i < 400; i++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();
                player.Play(clips[i % 2], new SoundPlacement(-(i % 12), i * 7 % 360, 500 + (i % 20) * 900));
                rig.Session.Pump(2);
                total += GC.GetAllocatedBytesForCurrentThread() - before;
            }

            return total;
        }

        _ = Measure();
        var started = player.Started;
        var measured = Measure();

        Assert.Equal(started + 400, player.Started);
        Assert.Equal(0, measured);
        if (Budgets.Recorded)
        {
            Budgets.Check("audio", "sound-play-and-mix", measured, Path.Combine(Path.GetDirectoryName(Here())!, "..", "Budgets.cs"));
        }
    }

    private static string Here([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    private static SoundClip Noise(double seconds, int seed = 7) => new("noise", AudioSignal.Noise(seconds, seed: seed).Left);

    private static SoundPlayer Linked(AudioRig rig, string name, out uint sinkId)
    {
        var player = SoundPlayer.Create(rig.Session, name, out var error) ?? throw new InvalidOperationException(error);
        var sink = rig.Node(AudioDaemonFixture.SinkName);
        sinkId = sink.Id;
        rig.Configure(player.NodeId, output: true);
        rig.Configure(sink.Id, output: false, monitor: true);
        rig.Link(player.NodeId, sink.Id);

        return player;
    }

    private static (float[] Left, float[] Right) RecordWhile(AudioRig rig, string name, Action play)
    {
        var wav = Path.Combine(rig.Directory, $"{name}.wav");
        var recorder = rig.Record(wav, $"basin-test-{name}");
        var node = rig.Node($"basin-test-{name}");
        rig.Configure(node.Id, output: false);
        rig.Link(rig.Node(AudioDaemonFixture.SinkName).Id, node.Id, monitor: true);
        rig.Pump(TimeSpan.FromMilliseconds(300));
        play();
        rig.Pump(TimeSpan.FromMilliseconds(1200));
        AudioRig.Stop(recorder);
        var take = AudioSignal.ReadWav(wav);
        return Loudest(take, AudioSignal.Rate);
    }

    private static (float[] Left, float[] Right) Loudest((float[] Left, float[] Right) take, int window)
    {
        var best = 0;
        var bestEnergy = -1.0;
        for (var start = 0; start + window <= take.Left.Length; start += window / 4)
        {
            double energy = 0;
            for (var i = start; i < start + window; i++)
            {
                energy += (take.Left[i] * (double)take.Left[i]) + (take.Right[i] * (double)take.Right[i]);
            }

            if (energy > bestEnergy)
            {
                bestEnergy = energy;
                best = start;
            }
        }

        var end = Math.Min(take.Left.Length, best + window);
        return (take.Left[best..end], take.Right[best..end]);
    }

    private static void WritePcm8(string path, int rate, byte[] samples)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + samples.Length);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(rate);
        writer.Write(rate);
        writer.Write((short)1);
        writer.Write((short)8);
        writer.Write("data"u8);
        writer.Write(samples.Length);
        writer.Write(samples);
    }

    private static void WritePcm16(string path, int rate, short[] samples)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + (samples.Length * 2));
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(rate);
        writer.Write(rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(samples.Length * 2);
        foreach (var sample in samples)
        {
            writer.Write(sample);
        }
    }
}
