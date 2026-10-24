using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace TinyComp.Tests;

public sealed class WorkspaceIpcTests
{
    [Fact]
    public void Send_to_workspace_and_workspace_move_reach_the_model()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "linux only");
        Assert.SkipWhen(OnPath("weston-simple-shm") is null, "weston-simple-shm is not on PATH");
        var tinycomp = Path.Combine(AppContext.BaseDirectory, "tinycomp");
        Assert.SkipUnless(File.Exists(tinycomp), "tinycomp has not been built beside the tests");
        var basinctl = Sample("BasinCtl", "basinctl");
        var runtime = Path.Combine("/tmp", $"basin-ws-{Guid.NewGuid():N}"[..22]);
        Directory.CreateDirectory(runtime);
        File.SetUnixFileMode(runtime, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var info = new ProcessStartInfo(tinycomp)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { "--backend", "headless", "--renderer", "pixman", "--config", "false", "--outputs", "2", "-c", "weston-simple-shm" })
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment["XDG_RUNTIME_DIR"] = runtime;
        info.Environment["BASIN_MUTE"] = "1";
        info.Environment.Remove("WAYLAND_DISPLAY");
        info.Environment.Remove("DISPLAY");
        using var compositor = Process.Start(info)!;
        compositor.OutputDataReceived += (_, _) => { };
        compositor.ErrorDataReceived += (_, _) => { };
        compositor.BeginOutputReadLine();
        compositor.BeginErrorReadLine();
        try
        {
            var socket = Path.Combine(runtime, "basin-wayland-0.sock");
            string Call(string json) => Run(basinctl, "--socket", socket, "raw", json);

            var window = 0UL;
            var deadline = Environment.TickCount64 + 20_000;
            while (window == 0 && Environment.TickCount64 < deadline)
            {
                if (File.Exists(socket) && Parse(Call("""{"method":"windows/list"}""")) is { } list &&
                    list.TryGetProperty("windows", out var windows) && windows.GetArrayLength() > 0)
                {
                    window = windows[0].GetProperty("id").GetUInt64();
                }
                else
                {
                    Thread.Sleep(200);
                }
            }

            Assert.NotEqual(0UL, window);
            var groups = Groups(Call("""{"method":"workspaces/list"}"""));
            Assert.Equal(2, groups.Count);
            var (first, second) = (groups[0].Id, groups[1].Id);
            Assert.Equal("{}", Call($$$"""{"method":"workspaces/create","params":{"group":{{{first}}},"name":"two"}}""").Trim());
            var two = Groups(Call("""{"method":"workspaces/list"}"""))[0].Workspaces.Single(w => w.Name == "two").Id;

            Assert.Equal("{}", Call($$$"""{"method":"windows/send-to-workspace","params":{"id":{{{window}}},"workspace":{{{two}}}}}""").Trim());
            Assert.Contains(window, Groups(Call("""{"method":"workspaces/list"}"""))[0].Workspaces.Single(w => w.Id == two).Members);

            Assert.Equal("{}", Call($$$"""{"method":"workspaces/move","params":{"id":{{{two}}},"group":{{{second}}}}}""").Trim());
            var after = Groups(Call("""{"method":"workspaces/list"}"""));
            Assert.DoesNotContain(after[0].Workspaces, w => w.Id == two);
            Assert.Contains(window, after[1].Workspaces.Single(w => w.Id == two).Members);

            Call("""{"method":"session/quit"}""");
            Assert.True(compositor.WaitForExit(20_000), "tinycomp did not quit");
        }
        finally
        {
            if (!compositor.HasExited)
            {
                compositor.Kill(entireProcessTree: true);
                compositor.WaitForExit(5000);
            }

            Directory.Delete(runtime, recursive: true);
        }
    }

    private static JsonElement? Parse(string json)
    {
        try
        {
            return JsonDocument.Parse(json).RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<(ulong Id, List<(ulong Id, string Name, List<ulong> Members)> Workspaces)> Groups(string json) =>
        [.. Parse(json)!.Value.GetProperty("groups").EnumerateArray().Select(group => (
            group.GetProperty("id").GetUInt64(),
            group.GetProperty("workspaces").EnumerateArray().Select(workspace => (
                workspace.GetProperty("id").GetUInt64(),
                workspace.GetProperty("name").GetString()!,
                workspace.GetProperty("members").EnumerateArray().Select(member => member.GetUInt64()).ToList())).ToList()))];

    private static string? OnPath(string name) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(':')
            .Select(directory => Path.Combine(directory, name))
            .FirstOrDefault(File.Exists);

    private static string Run(string path, params string[] arguments)
    {
        var info = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(20_000), $"{Path.GetFileName(path)} did not exit");
        return output.Result + errors.Result;
    }

    private static string Sample(string project, string name)
    {
        var framework = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        var directory = AppContext.BaseDirectory;
        while (directory is not null && !File.Exists(Path.Combine(directory, "basin.slnx")))
        {
            directory = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar));
        }

        Assert.NotNull(directory);
        var path = Path.Combine(directory, "samples", project, "bin", framework.Parent!.Name, framework.Name, name);
        Assert.SkipUnless(File.Exists(path), $"{name} has not been built beside the tests");
        return path;
    }
}
