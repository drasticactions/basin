using System.Globalization;
using Basin.Diagnostics;
using Xunit;
using static DungeonWm.Tests.ProcessTests;

namespace DungeonWm.Tests;

public sealed class InspectorTests
{
    public static TheoryData<string> Rows => new() { "gl", "vulkan" };

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task Opening_the_inspector_frees_the_cursor_and_closing_it_restores_the_mode(string row)
    {
        var (compositor, client) = Prerequisites(row, "ssdwin");
        using var session = new Session(compositor, row, client);
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("HUNG ssdwin", StringComparison.Ordinal)));
        Assert.NotNull(await session.WaitForAsync(line => line == "MODE crosshair"));

        await session.SendAsync("inspector open");
        Assert.NotNull(await session.WaitForAsync(line => line == "MODE inspector", fresh: false));
        var state = await session.WaitForAsync(line => line.StartsWith("INSPECTOR open=yes", StringComparison.Ordinal));
        Assert.NotNull(state);
        Assert.Contains($"host={row}", state!, StringComparison.Ordinal);

        var mark = session.Lines.Count;
        await session.SendAsync("inspector close");
        Assert.NotNull(await session.WaitForAsync(line => line == "MODE crosshair", since: mark));

        mark = session.Lines.Count;
        await Chord(session);
        Assert.NotNull(await session.WaitForAsync(line => line == "ACTION inspector", since: mark));
        Assert.NotNull(await session.WaitForAsync(line => line == "MODE inspector", since: mark));
        mark = session.Lines.Count;
        await Chord(session);
        Assert.NotNull(await session.WaitForAsync(line => line == "MODE crosshair", since: mark));

        await session.Quit();
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task A_touch_on_a_panel_close_box_closes_the_panel(string row)
    {
        var (compositor, client) = Prerequisites(row, "ssdwin");
        using var session = new Session(compositor, row, client);
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("HUNG ssdwin", StringComparison.Ordinal)));
        await session.SendAsync("inspector open");
        Assert.NotNull(await session.WaitForAsync(line => line == "MODE inspector"));
        await session.SendAsync("inspector-panel windows on");
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("INSPECTOR ", StringComparison.Ordinal) && line.Contains("Windows", StringComparison.Ordinal)));
        await Task.Delay(600, TestContext.Current.CancellationToken);

        await session.SendAsync("ipc input/touch {\"kind\":\"down\",\"id\":1,\"x\":1071,\"y\":50}");
        await session.SendAsync("ipc input/touch {\"kind\":\"frame\"}");
        await Task.Delay(100, TestContext.Current.CancellationToken);
        await session.SendAsync("ipc input/touch {\"kind\":\"up\",\"id\":1}");
        await session.SendAsync("ipc input/touch {\"kind\":\"frame\"}");
        await Task.Delay(400, TestContext.Current.CancellationToken);
        var state = await session.WaitForAsync(
            line => line.StartsWith("INSPECTOR open=yes", StringComparison.Ordinal) && !line.Contains("Windows", StringComparison.Ordinal),
            poke: "inspector-state", fresh: true);
        Assert.True(state is not null, "a touch on the close box did not close the Windows panel");
        await session.Quit();
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task A_pose_gizmo_drag_moves_a_thrown_window(string row)
    {
        var (compositor, client) = Prerequisites(row, "ssdwin");
        using var session = new Session(compositor, row, client);
        Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("MAPPED ssdwin", StringComparison.Ordinal)));
        await session.SendAsync("throw ssdwin 1 2 5");
        Assert.NotNull(await session.WaitForAsync(line => line == "DETACHED ssdwin"));
        Assert.NotNull(await session.WaitForAsync(
            line => line.StartsWith("PHYSICS awake=no", StringComparison.Ordinal) && line.Contains("bodies=1", StringComparison.Ordinal),
            poke: "physics-state"));
        await Aim(session);

        await session.SendAsync("inspector open");
        Assert.NotNull(await session.WaitForAsync(line => line == "MODE inspector"));
        await session.SendAsync("inspector-panel physics on");
        await session.SendAsync("inspector-panel pose on");
        await session.SendAsync("inspector-panel camera off");
        await Task.Delay(400, TestContext.Current.CancellationToken);

        var pose = await session.WaitForAsync(line => line.StartsWith("POSE ssdwin", StringComparison.Ordinal), poke: "inspector-pose", fresh: true);
        Assert.NotNull(pose);
        Assert.DoesNotContain("screen=none", pose!, StringComparison.Ordinal);
        var screen = pose!.Split("screen=")[1].Split(',');
        var x = int.Parse(screen[0], CultureInfo.InvariantCulture);
        var y = int.Parse(screen[1], CultureInfo.InvariantCulture);
        var before = await Body(session);

        await session.SendAsync($"move {x} {y}");
        await Task.Delay(400, TestContext.Current.CancellationToken);
        await session.SendAsync("button 272 true");
        for (var step = 1; step <= 8; step++)
        {
            await session.SendAsync($"move {x + (step * 12)} {y}");
            await Task.Delay(400, TestContext.Current.CancellationToken);
        }

        await session.SendAsync("button 272 false");
        await Task.Delay(400, TestContext.Current.CancellationToken);
        var after = await Body(session);
        var moved = MathF.Sqrt(((after.X - before.X) * (after.X - before.X)) + ((after.Z - before.Z) * (after.Z - before.Z)));
        Assert.True(moved > 0.15f, $"the window moved {moved:F2} m across the floor, from {before} to {after}");

        await session.Quit();
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task Golden_the_inspector_over_the_world_through_shotraw(string row)
    {
        var (compositor, client) = Prerequisites(row, "ssdwin");
        var directory = Directory.CreateTempSubdirectory("dungeon-wm-inspector-");
        try
        {
            var file = Path.Combine(directory.FullName, "dungeon-wm.toml");
            File.WriteAllText(file, "[world]\ntorch_flicker = 0\n\n[inspector]\nopen = true\npanels = [\"camera\", \"rooms\"]\n");
            using var session = new Session(compositor, row, client, "--config", file);
            Assert.NotNull(await session.WaitForAsync(line => line.StartsWith("HUNG ssdwin", StringComparison.Ordinal)));
            Assert.NotNull(await session.WaitForAsync(line => line == "MODE inspector", poke: "mode"));
            await session.SettleAsync();
            await Task.Delay(600, TestContext.Current.CancellationToken);
            await session.SettleAsync();
            var path = Path.Combine(directory.FullName, "inspector.png");
            Assert.NotNull(await session.WaitForAsync(line => line.StartsWith($"SHOTRAW {path} ", StringComparison.Ordinal), poke: $"shotraw {path}", fresh: true));
            var (rgba, width, height) = PngCodec.Decode(File.ReadAllBytes(path));
            var (crop, cropWidth, cropHeight) = Crop(rgba, width, [(0, 0, 1280, 20), (50, 466, 516, 254), (590, 466, 516, 254)]);
            Compare(crop, cropWidth, cropHeight, $"dungeon-wm-inspector-{row}");
            await session.Quit();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static async Task Aim(Session session)
    {
        var body = await Body(session);
        var camera = await session.WaitForAsync(l => l.StartsWith("CAMERA ", StringComparison.Ordinal), poke: "camera", fresh: true);
        Assert.NotNull(camera);
        var fields = camera!.Split(' ');
        var (x, y, z) = (float.Parse(fields[1], CultureInfo.InvariantCulture), float.Parse(fields[2], CultureInfo.InvariantCulture), float.Parse(fields[3], CultureInfo.InvariantCulture));
        var yaw = float.Parse(fields[4]["yaw=".Length..], CultureInfo.InvariantCulture);
        var pitch = float.Parse(fields[5]["pitch=".Length..], CultureInfo.InvariantCulture);
        var (dx, dy, dz) = (body.X - x, body.Y - y, body.Z - z);
        var wantYaw = MathF.Atan2(-dx, -dz) * 180 / MathF.PI;
        var wantPitch = MathF.Atan2(dy, MathF.Sqrt((dx * dx) + (dz * dz))) * 180 / MathF.PI;
        var turn = Math.IEEERemainder(wantYaw - yaw, 360);
        await session.SendAsync(string.Create(CultureInfo.InvariantCulture, $"look {turn:F2} {wantPitch - pitch:F2}"));
        await Task.Delay(400, TestContext.Current.CancellationToken);
    }

    private static async Task Chord(Session session)
    {
        await session.SendAsync("key 125 true");
        await session.SendAsync("key 88 true");
        await session.SendAsync("key 88 false");
        await session.SendAsync("key 125 false");
    }

    private static async Task<(float X, float Y, float Z)> Body(Session session)
    {
        var line = await session.WaitForAsync(l => l.StartsWith("BODY ssdwin ", StringComparison.Ordinal), poke: "physics-state", fresh: true);
        Assert.NotNull(line);
        var at = line!.Split("at=")[1].Split(' ')[0].Split(',');
        return (float.Parse(at[0], CultureInfo.InvariantCulture), float.Parse(at[1], CultureInfo.InvariantCulture), float.Parse(at[2], CultureInfo.InvariantCulture));
    }

    private static (byte[] Rgba, int Width, int Height) Crop(byte[] rgba, int width, (int X, int Y, int Width, int Height)[] boxes)
    {
        var cropWidth = boxes.Max(box => box.Width);
        var cropHeight = boxes.Sum(box => box.Height);
        var crop = new byte[cropWidth * cropHeight * 4];
        var top = 0;
        foreach (var (x, y, w, h) in boxes)
        {
            for (var line = 0; line < h; line++)
            {
                Array.Copy(rgba, (((y + line) * width) + x) * 4, crop, ((top + line) * cropWidth) * 4, w * 4);
            }

            top += h;
        }

        return (crop, cropWidth, cropHeight);
    }

    private static void Compare(byte[] rgba, int width, int height, string name)
    {
        var path = Path.Combine(Repository(), "tests", "DungeonWm.Tests", "Goldens", $"{name}.png");
        if (Environment.GetEnvironmentVariable("BASIN_UPDATE_GOLDENS") == "1")
        {
            File.WriteAllBytes(path, PngCodec.Encode(rgba, width, height));
            return;
        }

        Assert.True(File.Exists(path), $"Golden '{name}' missing. Run once with BASIN_UPDATE_GOLDENS=1 and commit {path}.");
        var (expected, expectedWidth, expectedHeight) = PngCodec.Decode(File.ReadAllBytes(path));
        Assert.Equal((expectedWidth, expectedHeight), (width, height));
        var differing = 0;
        for (var i = 0; i < expected.Length; i += 4)
        {
            var delta = Math.Max(Math.Abs(expected[i] - rgba[i]), Math.Max(Math.Abs(expected[i + 1] - rgba[i + 1]), Math.Abs(expected[i + 2] - rgba[i + 2])));
            if (delta > 6)
            {
                differing++;
            }
        }

        if (differing > 0)
        {
            var actual = Path.Combine(Path.GetTempPath(), $"{name}-actual.png");
            File.WriteAllBytes(actual, PngCodec.Encode(rgba, width, height));
            Assert.Fail($"Golden '{name}' mismatch: {differing} pixels beyond 6. Actual written to {actual}.");
        }
    }
}
