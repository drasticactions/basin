using System.Runtime.CompilerServices;
using Basin.Diagnostics;
using Xunit;
using static TabWm.Tests.Prerequisites;

namespace TabWm.Tests;

public sealed class GoldenTests
{
    private static readonly string[] Apps = ["Alpha Terminal", "Beta Browser", "Gamma Editor", "Delta Mail"];

    public static TheoryData<string> Rows() => ["pixman", "gl", "vulkan", "skia", "skia-gl", "skia-vulkan", "skia-graphite"];

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task The_strip_and_the_launcher_match_their_goldens(string renderer)
    {
        SkipUnlessRenderer(renderer);
        var home = Directory.CreateTempSubdirectory("tab-wm-golden-").FullName;
        try
        {
            var data = Fixture(home);
            var config = Path.Combine(home, "tab-wm.toml");
            File.WriteAllText(config, "[theme]\nengine = \"managed\"\nvariant = \"light\"\n[launcher]\npinned = [\"alpha.desktop\", \"gamma.desktop\"]\n[session]\nrestore = false\n");
            var environment = new Dictionary<string, string>
            {
                ["XDG_DATA_HOME"] = data,
                ["XDG_DATA_DIRS"] = data,
                ["XDG_CURRENT_DESKTOP"] = "tab-wm",
                ["LANG"] = "C.UTF-8",
            };
            if (VulkanDrivers() is { Length: > 0 } drivers)
            {
                environment["VK_DRIVER_FILES"] = drivers;
            }

            using var session = new Session(Compositor(), renderer, home, environment, "--no-xwayland", "--config", config);
            var chosen = await session.WaitForPrefixAsync("RENDERER ");
            Assert.SkipWhen(chosen!.Split(' ')[1] != renderer, $"{renderer} fell back to {chosen.Split(' ')[1]}");
            await session.DisplayAsync();
            Assert.NotNull(await session.WaitForPrefixAsync("LAUNCHER entries=4 pinned=2"));
            await session.SendAsync("clock 12:00");
            await session.SendAsync("move 5000 5000");
            await Shoot(session, "launcher", renderer);

            var tabs = await session.TabsAsync();
            Assert.True(tabs.Count == 1, string.Join(" | ", tabs));
            var first = Field(tabs[0], "id");
            await session.AskAsync($"tab rename {first} Mail", "TAB ");
            var second = Field((await session.AskAsync("tab new", "TAB ")).Single(), "id");
            await session.AskAsync($"tab rename {second} Documents", "TAB ");
            await session.AskAsync("tab new", "TAB ");
            await session.SendAsync("launcher show");
            await session.AskAsync("search a", "RESULT ");
            await Shoot(session, "overlay", renderer);
            await session.QuitCleanlyAsync();
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    private static string VulkanDrivers()
    {
        var files = new List<string>();
        foreach (var directory in new[] { "/usr/share/vulkan/icd.d", "/etc/vulkan/icd.d", "/usr/local/share/vulkan/icd.d" })
        {
            if (Directory.Exists(directory))
            {
                files.AddRange(Directory.GetFiles(directory, "*.json"));
            }
        }

        return string.Join(':', files);
    }

    private static string Fixture(string home)
    {
        var data = Path.Combine(home, "share");
        var applications = Path.Combine(data, "applications");
        Directory.CreateDirectory(applications);
        string[] categories = ["System;TerminalEmulator;", "Network;WebBrowser;", "Utility;TextEditor;", "Network;Email;"];
        for (var i = 0; i < Apps.Length; i++)
        {
            var id = Apps[i].Split(' ')[0].ToLowerInvariant();
            File.WriteAllText(Path.Combine(applications, $"{id}.desktop"),
                $"[Desktop Entry]\nType=Application\nName={Apps[i]}\nExec=/bin/true {id}\nIcon=application-x-executable\nCategories={categories[i]}\n");
        }

        return data;
    }

    private static async Task Shoot(Session session, string scene, string renderer)
    {
        await session.SettleAsync();
        var path = Path.Combine(session.Home, $"{scene}.png");
        var mark = session.Mark();
        await session.SendAsync($"shotraw {path}");
        var reply = await session.WaitForPrefixAsync("SHOTRAW ", mark);
        Assert.True(reply is not null && reply.StartsWith($"SHOTRAW {path} ", StringComparison.Ordinal), $"[{reply}] [{path}]");
        for (var i = 0; i < 50 && !File.Exists(path); i++)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        AssertMatches(path, $"{scene}-{renderer}", renderer is "pixman" or "skia" ? 0 : 2, record: true);
    }

    private static void AssertMatches(string actualPath, string name, int tolerance, bool record, [CallerFilePath] string sourcePath = "")
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
            var kept = Path.Combine(Path.GetTempPath(), $"tab-wm-golden-{name}-actual.png");
            File.Copy(actualPath, kept, overwrite: true);
            Assert.Fail($"Golden '{name}' mismatch: {differing} pixels beyond ±{tolerance} (max channel delta {maxDelta}). Actual kept at {kept}.");
        }
    }
}
