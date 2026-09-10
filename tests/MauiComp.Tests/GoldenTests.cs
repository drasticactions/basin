using System.Runtime.CompilerServices;
using Basin.Diagnostics;
using Xunit;

namespace MauiComp.Tests;

public sealed class GoldenTests
{
    [Theory]
    [InlineData("pixman")]
    [InlineData("gl")]
    [InlineData("vulkan")]
    public async Task The_panel_matches_its_golden(string renderer)
    {
        var (compositor, _) = HeadlessSessionTests.Require();
        Assert.SkipWhen(renderer != "pixman" && !File.Exists("/dev/dri/renderD128"), "no render node");

        var actual = Path.Combine(Path.GetTempPath(), $"maui-comp-panel-{renderer}-{Environment.ProcessId}.png");
        await Shoot(compositor, renderer, actual);

        AssertMatches(actual, $"panel-{renderer}", renderer == "pixman" ? 0 : 2);
        File.Delete(actual);
    }

    [Fact]
    public async Task The_published_binary_draws_the_same_panel_as_the_jit_build()
    {
        HeadlessSessionTests.Require();
        var published = Session.Locate(Path.Combine("linux-x64", "publish", "maui-comp"))
            ?? FindPublished();
        Assert.SkipWhen(published is null, "maui-comp has not been published with NativeAOT");

        var actual = Path.Combine(Path.GetTempPath(), $"maui-comp-panel-aot-{Environment.ProcessId}.png");
        await Shoot(published!, "pixman", actual);

        AssertMatches(actual, "panel-pixman", 0, record: false);
        File.Delete(actual);
    }

    private static async Task Shoot(string compositor, string renderer, string path)
    {
        using var session = new Session(compositor, renderer);
        await session.DisplayAsync();
        var chosen = await session.WaitForAsync(line => line.StartsWith("RENDERER ", StringComparison.Ordinal));
        Assert.NotNull(chosen);
        Assert.SkipWhen(chosen!.Split(' ')[1] != renderer, $"{renderer} fell back to {chosen.Split(' ')[1]}");

        await session.SendAsync("clock 12:00");
        await Task.Delay(400, TestContext.Current.CancellationToken);
        await session.SendAsync($"shot {path}");
        for (var i = 0; i < 50 && !File.Exists(path); i++)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        Assert.True(File.Exists(path), "the compositor wrote the screenshot");
        await HeadlessSessionTests.AssertCleanExit(session);
    }

    private static string? FindPublished()
    {
        var directory = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && directory is not null; i++)
        {
            var candidate = Path.Combine(
                directory, "samples", "MauiComp", "bin", "Release", "net11.0", "linux-x64", "publish", "maui-comp");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar));
        }

        return null;
    }

    private static void AssertMatches(
        string actualPath, string name, int tolerance, bool record = true, [CallerFilePath] string sourcePath = "")
    {
        var goldenPath = Path.Combine(Path.GetDirectoryName(sourcePath)!, "Goldens", $"{name}.png");
        var actualPng = File.ReadAllBytes(actualPath);

        if (record && Environment.GetEnvironmentVariable("BASIN_UPDATE_GOLDENS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);
            File.WriteAllBytes(goldenPath, actualPng);
            return;
        }

        if (!File.Exists(goldenPath))
        {
            throw new FileNotFoundException(
                $"Golden '{name}' missing. Run once with BASIN_UPDATE_GOLDENS=1 and commit {goldenPath}.");
        }

        var (expected, width, height) = PngCodec.Decode(File.ReadAllBytes(goldenPath));
        var (actual, actualWidth, actualHeight) = PngCodec.Decode(actualPng);
        if (width != actualWidth || height != actualHeight)
        {
            throw new InvalidOperationException($"Golden '{name}' is {width}x{height}, the screenshot is {actualWidth}x{actualHeight}.");
        }

        var differing = 0;
        var maxDelta = 0;
        for (var i = 0; i < expected.Length; i += 4)
        {
            var pixelDelta = 0;
            for (var channel = 0; channel < 3; channel++)
            {
                pixelDelta = Math.Max(pixelDelta, Math.Abs(expected[i + channel] - actual[i + channel]));
            }

            if (pixelDelta > tolerance)
            {
                differing++;
            }

            maxDelta = Math.Max(maxDelta, pixelDelta);
        }

        if (differing > 0)
        {
            var kept = Path.Combine(Path.GetTempPath(), $"maui-comp-golden-{name}-actual.png");
            File.Copy(actualPath, kept, overwrite: true);
            throw new InvalidOperationException(
                $"Golden '{name}' mismatch: {differing} pixels beyond ±{tolerance} (max channel delta {maxDelta}). Actual kept at {kept}.");
        }
    }
}
