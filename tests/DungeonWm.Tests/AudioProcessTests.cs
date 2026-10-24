using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Basin.Tests;
using Xunit;

namespace DungeonWm.Tests;

public sealed class AudioProcessTests(AudioDaemonFixture daemon) : IClassFixture<AudioDaemonFixture>
{
    private const string Row = "gl";

    [Fact]
    public async Task A_launched_stream_plays_from_its_window_and_turns_with_the_camera()
    {
        using var session = Start(out var tone, out var ssdwin);
        await session.SendAsync($"ipc process/spawn {Spawn($"pw-play --volume 0 {tone} & {ssdwin}")}");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("AUDIO MATCH ", StringComparison.Ordinal) && line.EndsWith(" via=launch-tag", StringComparison.Ordinal)));

        await session.SendAsync("look -45 0");
        var left = Azimuth(await session.WaitForAsync(line => line.StartsWith("VOICE ", StringComparison.Ordinal), poke: "audio", fresh: true));
        await session.SendAsync("look 90 0");
        var right = Azimuth(await session.WaitForAsync(line => line.StartsWith("VOICE ", StringComparison.Ordinal), poke: "audio", fresh: true));

        Assert.True(left > 0, $"the window is at {left} degrees after turning right");
        Assert.True(right < 0, $"the window is at {right} degrees after turning left");
        Assert.InRange(left - right, 88, 92);

        await session.SendAsync("tp 2");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("VOICE ", StringComparison.Ordinal) && line.Contains(" path=doorways:1 ", StringComparison.Ordinal), poke: "audio", fresh: true));

        await session.Quit();
        Assert.Empty(VoiceNodes());
    }

    [Fact]
    public async Task A_window_in_takeover_plays_centered_at_full_gain()
    {
        using var session = Start(out var tone, out var ssdwin);
        await session.SendAsync($"ipc process/spawn {Spawn($"pw-play --volume 0 {tone} & exec {ssdwin}")}");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("AUDIO MATCH ", StringComparison.Ordinal)));
        await session.SendAsync("look -60 0");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("VOICE ", StringComparison.Ordinal) && !line.Contains(" az=0 ", StringComparison.Ordinal), poke: "audio", fresh: true));

        await session.SendAsync("takeover ssdwin");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("TAKEOVER ssdwin on ", StringComparison.Ordinal)));
        var voice = await session.WaitForAsync(line => line.StartsWith("VOICE ", StringComparison.Ordinal), poke: "audio", fresh: true);

        Assert.Contains(" az=0 el=0 ", voice!, StringComparison.Ordinal);
        Assert.Contains(" gain=0.00 ", voice, StringComparison.Ordinal);
        Assert.Contains(" cutoff=20000 wet=-inf", voice, StringComparison.Ordinal);
        await session.Quit();
    }

    [Fact]
    public async Task Rehanging_in_a_room_of_another_size_swaps_the_voice_for_that_room_reverb()
    {
        using var session = Start(out var tone, out var ssdwin);
        await session.SendAsync($"ipc process/spawn {Spawn($"pw-play --volume 0 {tone} & exec {ssdwin}")}");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("AUDIO MATCH ", StringComparison.Ordinal)));
        var before = await session.WaitForAsync(line => line.StartsWith("VOICE ", StringComparison.Ordinal) && line.Contains(" region=room1 ", StringComparison.Ordinal), poke: "audio", fresh: true);
        Assert.NotNull(before);
        Assert.DoesNotContain(" wet=-inf ", before!, StringComparison.Ordinal);
        var names = VoiceNodes();

        await session.SendAsync("hang ssdwin 3 any");
        var reverb = Field(before, "reverb");
        var after = await session.WaitForAsync(line => line.StartsWith("VOICE ", StringComparison.Ordinal) && line.Contains(" region=room3 ", StringComparison.Ordinal) && Field(line, "reverb") != reverb, poke: "audio", fresh: true);
        Assert.NotNull(after);
        Assert.True(await Eventually(() => VoiceNodes() is { Count: 2 } now && !now.Intersect(names).Any()), string.Join(", ", VoiceNodes()));
        await session.Quit();
        Assert.Empty(VoiceNodes());
    }

    [Fact]
    public async Task Past_the_voice_cap_the_quietest_window_plays_plain()
    {
        var config = Path.Combine(daemon.RuntimeDirectory, "cap.toml");
        await File.WriteAllTextAsync(config, "[audio]\nmax_voices = 1\n", TestContext.Current.CancellationToken);
        using var session = Start(out var tone, out var ssdwin, "--config", config);
        await session.SendAsync($"ipc process/spawn {Spawn($"pw-play --volume 0 {tone} & exec {ssdwin}")}");
        var first = await session.WaitForAsync(line => line.StartsWith("AUDIO MATCH ", StringComparison.Ordinal));
        Assert.NotNull(first);
        await session.SendAsync($"ipc process/spawn {Spawn($"pw-play --volume 0 {tone} & exec {ssdwin}")}");
        var window = first![first.IndexOf(" window=", StringComparison.Ordinal)..first.IndexOf(" via=", StringComparison.Ordinal)];
        Assert.True(await session.WaitForAsync(line => line.StartsWith("AUDIO MATCH ", StringComparison.Ordinal) && !line.Contains(window, StringComparison.Ordinal)) is not null, string.Join('\n', session.Lines));
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("AUDIO mode=auto voices=1 ", StringComparison.Ordinal), poke: "audio", fresh: true));

        var lines = session.Lines;
        var voices = lines.Skip(lines.ToList().FindLastIndex(line => line.StartsWith("AUDIO mode=", StringComparison.Ordinal))).Where(line => line.StartsWith("VOICE ", StringComparison.Ordinal)).ToList();
        Assert.True(voices.Count == 2, string.Join('\n', lines));
        Assert.Single(voices, line => line.EndsWith(" voice=none", StringComparison.Ordinal));
        await session.Quit();
        Assert.Empty(VoiceNodes());
    }

    [Fact]
    public async Task Auto_pans_on_a_sink_that_is_not_headphones_and_hrtf_builds_on_request()
    {
        Assert.SkipUnless(File.Exists("/usr/share/libmysofa/default.sofa"), "no SOFA file");
        using var session = Start(out var tone, out var ssdwin);
        await session.SendAsync($"ipc process/spawn {Spawn($"pw-play --volume 0 {tone} & exec {ssdwin}")}");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("AUDIO MATCH ", StringComparison.Ordinal)));
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("AUDIO mode=auto voices=1 ", StringComparison.Ordinal) && line.Contains(" graph=pan ", StringComparison.Ordinal), poke: "audio", fresh: true));

        await session.SendAsync("audio-mode hrtf");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("AUDIO mode=hrtf voices=1 ", StringComparison.Ordinal) && line.Contains(" graph=hrtf ", StringComparison.Ordinal)));
        await session.SendAsync("look -30 0");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("VOICE ", StringComparison.Ordinal) && line.Contains(" az=30 ", StringComparison.Ordinal), poke: "audio", fresh: true));
        await session.Quit();
        Assert.Empty(VoiceNodes());
    }

    [Fact]
    public async Task Auto_switches_to_hrtf_when_the_default_sink_becomes_headphones_and_back()
    {
        Assert.SkipUnless(File.Exists("/usr/share/libmysofa/default.sofa"), "no SOFA file");
        Assert.SkipWhen(Which("pw-cli") is null || Which("pw-metadata") is null, "pw-cli or pw-metadata is not installed");
        using var session = Start(out var tone, out var ssdwin);
        Run("pw-cli", "create-node", "adapter", "{ factory.name = support.null-audio-sink node.name = basin-test-headphones media.class = Audio/Sink object.linger = true audio.position = [ FL FR ] device.form-factor = headphones }");
        await session.SendAsync($"ipc process/spawn {Spawn($"pw-play --volume 0 {tone} & exec {ssdwin}")}");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("AUDIO MATCH ", StringComparison.Ordinal)));

        Run("pw-metadata", "0", "default.audio.sink", """{"name":"basin-test-headphones"}""", "Spa:String:JSON");
        Assert.NotNull(await session.WaitForAsync(line => line == "AUDIO output=headphones graph=hrtf"));
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("AUDIO mode=auto voices=1 ", StringComparison.Ordinal) && line.EndsWith(" graph=hrtf output=headphones", StringComparison.Ordinal), poke: "audio", fresh: true));

        Run("pw-cli", "create-node", "adapter", "{ factory.name = support.null-audio-sink node.name = basin-test-headphones-2 media.class = Audio/Sink object.linger = true audio.position = [ FL FR ] device.form-factor = headphones }");
        var moved = session.Lines.Count;
        Run("pw-metadata", "0", "default.audio.sink", """{"name":"basin-test-headphones-2"}""", "Spa:String:JSON");
        await Task.Delay(1000, TestContext.Current.CancellationToken);
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("AUDIO mode=auto voices=1 ", StringComparison.Ordinal) && line.EndsWith(" graph=hrtf output=headphones", StringComparison.Ordinal), poke: "audio", fresh: true));
        Assert.DoesNotContain(session.Lines.Skip(moved), line => line.StartsWith("AUDIO output=", StringComparison.Ordinal));

        Run("pw-metadata", "0", "default.audio.sink", $$"""{"name":"{{AudioDaemonFixture.SinkName}}"}""", "Spa:String:JSON");
        Assert.NotNull(await session.WaitForAsync(line => line == "AUDIO output=speakers graph=pan"));
        await session.Quit();
        Run("pw-metadata", "-d", "0", "default.audio.sink");
    }

    [Fact]
    public async Task A_missing_hrtf_file_falls_back_to_pan_and_says_why_once()
    {
        var config = Path.Combine(daemon.RuntimeDirectory, "nohrtf.toml");
        await File.WriteAllTextAsync(config, "[audio]\nmode = \"hrtf\"\nhrtf = \"/nonexistent.sofa\"\n", TestContext.Current.CancellationToken);
        using var session = Start(out var tone, out var ssdwin, "--config", config);
        Assert.NotNull(await session.WaitForAsync(line => line == "AUDIO hrtf unavailable: /nonexistent.sofa does not exist"));
        await session.SendAsync($"ipc process/spawn {Spawn($"pw-play --volume 0 {tone} & exec {ssdwin}")}");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("AUDIO MATCH ", StringComparison.Ordinal)));
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("AUDIO mode=hrtf voices=1 ", StringComparison.Ordinal) && line.Contains(" graph=pan ", StringComparison.Ordinal), poke: "audio", fresh: true));
        Assert.Single(session.Lines, line => line.StartsWith("AUDIO hrtf unavailable", StringComparison.Ordinal));
        await session.Quit();
    }

    [Fact]
    public async Task A_voice_raises_dungeonwm_audio_when_it_is_created_and_removed()
    {
        var socket = Path.Combine(daemon.RuntimeDirectory, $"events-{Guid.NewGuid():N}.sock");
        using var session = Start(out var tone, out var ssdwin, "--ipc", socket);
        var info = new ProcessStartInfo(ControlTests.Tool("basinctl"))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[] { "--socket", socket, "subscribe", "dungeonwm/audio" })
        {
            info.ArgumentList.Add(argument);
        }

        Assert.True(await Eventually(() => File.Exists(socket)));
        using var subscriber = Process.Start(info)!;
        var events = new List<string>();
        subscriber.OutputDataReceived += (_, e) =>
        {
            if (e.Data is { } line)
            {
                lock (events)
                {
                    events.Add(line);
                }
            }
        };
        subscriber.BeginOutputReadLine();
        await Task.Delay(500, TestContext.Current.CancellationToken);

        await session.SendAsync($"ipc process/spawn {Spawn($"pw-play --volume 0 {tone} & exec {ssdwin}")}");
        Assert.True(await Eventually(() => { lock (events) { return events.Any(line => line.Contains("\"created\"", StringComparison.Ordinal)); } }), "no created event");
        await session.SendAsync("audio-mode off");
        Assert.True(await Eventually(() => { lock (events) { return events.Any(line => line.Contains("\"removed\"", StringComparison.Ordinal)); } }), string.Join('\n', events));
        subscriber.Kill();
        await session.Quit();
    }

    [Fact]
    public async Task Walking_throwing_and_pressing_a_plaque_play_their_effects()
    {
        using var session = Start(out _, out var ssdwin, sounds: true);
        await session.SendAsync($"ipc process/spawn {Spawn($"exec {ssdwin}")}");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("HUNG ssdwin", StringComparison.Ordinal)));
        Assert.True(await Eventually(() => VoiceNodes().Any(name => name.EndsWith(".sounds", StringComparison.Ordinal))), string.Join(", ", VoiceNodes()));

        await session.SendAsync("key 17 true");
        await Task.Delay(1500, TestContext.Current.CancellationToken);
        await session.SendAsync("key 17 false");
        await session.SettleAsync();

        await session.SendAsync("throw ssdwin 0 1 3");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("THROW ssdwin", StringComparison.Ordinal)));
        var thrown = await session.WaitForAsync(line => line.StartsWith("SOUNDS ", StringComparison.Ordinal) && Count(line, "impacts") >= 1, poke: "sounds", fresh: true);
        Assert.NotNull(thrown);
        await session.SendAsync("plaque ssdwin rehang");
        var sounds = await session.WaitForAsync(line => line.StartsWith("SOUNDS ", StringComparison.Ordinal) && Count(line, "clicks") == 1, poke: "sounds", fresh: true);

        Assert.NotNull(sounds);
        Assert.StartsWith("SOUNDS state=on ", sounds!, StringComparison.Ordinal);
        Assert.InRange(Count(sounds, "steps"), 2, 6);
        Assert.True(Count(sounds, "hangs") >= 1, sounds);
        Assert.DoesNotContain(" room=none", sounds, StringComparison.Ordinal);
        await session.Quit();
        Assert.Empty(VoiceNodes());
    }

    [Fact]
    public async Task Basin_mute_keeps_the_effects_silent()
    {
        using var session = Start(out _, out var ssdwin);
        Assert.NotNull(await session.WaitForAsync(line => line == "SOUNDS muted"));
        await session.SendAsync($"ipc process/spawn {Spawn($"exec {ssdwin}")}");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("HUNG ssdwin", StringComparison.Ordinal)));
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("SOUNDS state=muted played=0 ", StringComparison.Ordinal), poke: "sounds", fresh: true));
        Assert.DoesNotContain(VoiceNodes(), name => name.EndsWith(".sounds", StringComparison.Ordinal));
        await session.Quit();
    }

    [Fact]
    public async Task Without_a_daemon_audio_reports_unavailable_and_the_compositor_runs()
    {
        var (compositor, ssdwin) = ProcessTests.Prerequisites(Row, "ssdwin");
        using var session = new ProcessTests.Session(compositor, Row, ssdwin);
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("AUDIO unavailable: ", StringComparison.Ordinal)));
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("MAPPED ssdwin", StringComparison.Ordinal)));
        Assert.NotNull(await session.WaitForAsync(line => line == "AUDIO mode=off voices=0 unmatched=0", poke: "audio", fresh: true));
        await session.Quit();
    }

    private ProcessTests.Session Start(out string tone, out string ssdwin, params string[] extra) =>
        Start(out tone, out ssdwin, sounds: false, extra);

    private ProcessTests.Session Start(out string tone, out string ssdwin, bool sounds, params string[] extra)
    {
        daemon.SkipIfUnavailable();
        Assert.SkipWhen(Which("pw-play") is null, "pw-play is not installed");
        var (compositor, client) = ProcessTests.Prerequisites(Row, "ssdwin");
        ssdwin = client;
        tone = Path.Combine(daemon.RuntimeDirectory, "tone.wav");
        if (!File.Exists(tone))
        {
            var (left, right) = AudioSignal.Sine(440, 60);
            AudioSignal.WriteWav(tone, left, right);
        }

        var environment = new Dictionary<string, string> { ["PIPEWIRE_REMOTE"] = daemon.SocketPath, ["BASIN_MUTE"] = sounds ? "0" : "1" };
        return new ProcessTests.Session(compositor, Row, "true", environment, extra);
    }

    private static string Spawn(string command)
    {
        using var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteStartArray("argv");
            json.WriteStringValue("/bin/sh");
            json.WriteStringValue("-c");
            json.WriteStringValue(command);
            json.WriteEndArray();
            json.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static int Count(string line, string key) => int.Parse(Field(line, key), CultureInfo.InvariantCulture);

    private static string Field(string line, string key)
    {
        var start = line.IndexOf($" {key}=", StringComparison.Ordinal) + key.Length + 2;
        var end = line.IndexOf(' ', start);
        return end < 0 ? line[start..] : line[start..end];
    }

    private static async Task<bool> Eventually(Func<bool> condition)
    {
        for (var i = 0; i < 50; i++)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        return false;
    }

    private static float Azimuth(string? voice)
    {
        Assert.NotNull(voice);
        var start = voice!.IndexOf(" az=", StringComparison.Ordinal) + 4;
        var end = voice.IndexOf(' ', start);
        return float.Parse(voice[start..end], CultureInfo.InvariantCulture);
    }

    private List<string> VoiceNodes()
    {
        var info = daemon.Client("pw-dump");
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(10_000);
        var names = new List<string>();
        using var json = JsonDocument.Parse(output);
        foreach (var item in json.RootElement.EnumerateArray())
        {
            if (item.TryGetProperty("info", out var entry) && entry.ValueKind == JsonValueKind.Object &&
                entry.TryGetProperty("props", out var props) && props.TryGetProperty("node.name", out var name) &&
                name.GetString() is { } text && text.StartsWith("dungeon-wm.", StringComparison.Ordinal))
            {
                names.Add(text);
            }
        }

        return names;
    }

    private void Run(string program, params string[] arguments)
    {
        using var process = Process.Start(daemon.Client(program, arguments))!;
        process.StandardOutput.ReadToEnd();
        Assert.True(process.WaitForExit(10_000), $"{program} did not exit");
    }

    private static string? Which(string program)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(':'))
        {
            var candidate = Path.Combine(directory, program);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
