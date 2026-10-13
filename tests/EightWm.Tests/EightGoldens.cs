using System.Runtime.CompilerServices;
using Basin.Diagnostics;
using Xunit;

namespace EightWm.Tests;

internal static class EightGoldens
{
    public static TheoryData<string> Renderers() =>
        new("pixman", "gl", "vulkan", "skia", "skia-gl", "skia-vulkan", "skia-graphite");

    public static void SkipUnlessRenderer(string renderer)
    {
        if (renderer is "pixman" or "skia")
        {
            return;
        }

        var node = Environment.GetEnvironmentVariable("BASIN_RENDER_NODE") ?? "/dev/dri/renderD128";
        Assert.SkipUnless(File.Exists(node), $"{renderer} needs a render node");
    }

    public static async Task RequireRenderer(EightSession session, string renderer)
    {
        var chosen = await session.WaitForAsync(line => line.StartsWith("RENDERER ", StringComparison.Ordinal));
        if (chosen is null || chosen.Split(' ')[1] != renderer)
        {
            session.Dispose();
            Assert.Skip($"{renderer} fell back to {chosen}");
        }
    }

    public static async Task Shoot(EightSession session, string scene, string renderer)
    {
        await session.SettleAsync();
        var path = Path.Combine(Path.GetTempPath(), $"eight-wm-golden-{Environment.ProcessId}-{scene}-{renderer}.png");
        var mark = session.Mark();
        await session.SendAsync($"shotraw {path}");
        var reply = await session.WaitForAsync(line => line.StartsWith("SHOTRAW ", StringComparison.Ordinal), since: mark);
        Assert.True(reply is not null && reply.StartsWith($"SHOTRAW {path} ", StringComparison.Ordinal), $"[{reply}] [{path}]");
        try
        {
            AssertMatches(path, $"{scene}-{renderer}", renderer == "pixman" ? 0 : 2);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void AssertMatches(string actualPath, string name, int tolerance, [CallerFilePath] string sourcePath = "")
    {
        var goldenPath = Path.Combine(Path.GetDirectoryName(sourcePath)!, "Goldens", $"{name}.png");
        var actualPng = File.ReadAllBytes(actualPath);
        if (Environment.GetEnvironmentVariable("BASIN_UPDATE_GOLDENS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);
            File.WriteAllBytes(goldenPath, actualPng);
            return;
        }

        if (!File.Exists(goldenPath))
        {
            throw new FileNotFoundException($"Golden '{name}' missing. Run once with BASIN_UPDATE_GOLDENS=1 and commit {goldenPath}.");
        }

        var (expected, width, height) = PngCodec.Decode(File.ReadAllBytes(goldenPath));
        var (actual, actualWidth, actualHeight) = PngCodec.Decode(actualPng);
        Assert.True(width == actualWidth && height == actualHeight, $"Golden '{name}' is {width}x{height}, the screenshot is {actualWidth}x{actualHeight}.");
        var differing = 0;
        var maxDelta = 0;
        for (var i = 0; i < expected.Length; i += 4)
        {
            var delta = 0;
            for (var channel = 0; channel < 3; channel++)
            {
                delta = Math.Max(delta, Math.Abs(expected[i + channel] - actual[i + channel]));
            }

            differing += delta > tolerance ? 1 : 0;
            maxDelta = Math.Max(maxDelta, delta);
        }

        if (differing > 0)
        {
            var kept = Path.Combine(Path.GetTempPath(), $"eight-wm-golden-{name}-actual.png");
            File.Copy(actualPath, kept, overwrite: true);
            Assert.Fail($"Golden '{name}' mismatch: {differing} pixels beyond ±{tolerance} (max channel delta {maxDelta}). Actual kept at {kept}.");
        }
    }
}
