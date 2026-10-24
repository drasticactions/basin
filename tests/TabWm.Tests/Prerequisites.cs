using System.Diagnostics;
using Xunit;

namespace TabWm.Tests;

internal static class Prerequisites
{
    public static string Compositor()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the compositor is Linux only");
        var compositor = Session.Locate("tab-wm");
        Assert.SkipWhen(compositor is null, "tab-wm has not been built beside the tests");
        return compositor!;
    }

    public static string? RepoRoot()
    {
        var directory = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && directory is not null; i++)
        {
            if (File.Exists(Path.Combine(directory, "basin.slnx")))
            {
                return directory;
            }

            directory = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar));
        }

        return null;
    }

    public static string WlClient(string name)
    {
        var root = RepoRoot();
        Assert.SkipWhen(root is null, "the repository root was not found");
        var clients = Path.Combine(root!, "scripts", "wlclients");
        var binary = Path.Combine(clients, "bin", name);
        if (!File.Exists(binary))
        {
            var info = new ProcessStartInfo("make") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = clients };
            info.ArgumentList.Add($"bin/{name}");
            using var make = Process.Start(info)!;
            make.WaitForExit(120_000);
        }

        Assert.SkipUnless(File.Exists(binary), $"scripts/wlclients/bin/{name} could not be built");
        return binary;
    }

    public static string Program(string name)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(':'))
        {
            if (directory.Length > 0 && File.Exists(Path.Combine(directory, name)))
            {
                return Path.Combine(directory, name);
            }
        }

        Assert.Skip($"{name} is not installed");
        return name;
    }

    public static void SkipUnlessRenderer(string renderer)
    {
        if (renderer == "pixman" || renderer == "skia")
        {
            return;
        }

        var node = Environment.GetEnvironmentVariable("BASIN_RENDER_NODE") ?? "/dev/dri/renderD128";
        Assert.SkipUnless(File.Exists(node), $"{renderer} needs a render node");
    }

    public static string Field(string line, string key)
    {
        foreach (var word in line.Split(' '))
        {
            if (word.StartsWith(key + "=", StringComparison.Ordinal))
            {
                return word[(key.Length + 1)..];
            }
        }

        return string.Empty;
    }
}
