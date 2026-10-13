using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using Basin.Diagnostics;
using Basin.Effects;
using Basin;
using Xunit;

namespace TinyComp.Tests;

internal static class CanvasRows
{
    internal const int ZoneWidth = 154;

    internal const int ZoneHeight = 86;

    internal const string ScaleMode =
        "[canvas]\nenable = true\ngrid = \"never\"\nanimation_ms = 100\nwindow = \"scale\"\n";

    internal const string FourSides =
        "[canvas]\nenable = true\ngrid = \"never\"\nanimation_ms = 100\nsides = [\"left\", \"right\", \"top\", \"bottom\"]\n";

    internal static string? Panel([CallerFilePath] string sourcePath = "") => WlClient("panel", sourcePath);

    internal static CanvasWarpTransform LayoutMap()
    {
        var left = new CanvasWarp();
        left.Layout(ZoneWidth, -1, ZoneWidth, 640, 0.2, 0.25, 360);
        var right = new CanvasWarp(1);
        right.Layout(1280 - ZoneWidth, 1, ZoneWidth, 640, 0.2, 0.25, 360);
        var top = new CanvasWarp();
        top.Layout(ZoneHeight, -1, ZoneHeight, 360, 0.2, 0.25, 640);
        var bottom = new CanvasWarp(1);
        bottom.Layout(720 - ZoneHeight, 1, ZoneHeight, 360, 0.2, 0.25, 640);
        return new CanvasWarpTransform
        {
            Left = left,
            Right = right,
            Top = top,
            Bottom = bottom,
        };
    }

    internal const string OverviewConfig = "[overview]\ngrid = \"never\"\nanimation_ms = 100\nwall = \"slope\"\n";

    internal static Dictionary<string, string> Fields(string line)
    {
        var fields = new Dictionary<string, string>();
        foreach (var part in line.Split(' '))
        {
            var equals = part.IndexOf('=');
            if (equals > 0)
            {
                fields[part[..equals]] = part[(equals + 1)..];
            }
        }

        return fields;
    }

    internal static Dictionary<string, string> OverviewWindow(CanvasSession session)
    {
        session.Send("where");
        var line = session.WaitForLine("OVERVIEWWIN ");
        Assert.NotNull(line);
        return Fields(line!);
    }

    internal static void OpenOverview(CanvasSession session)
    {
        session.Send("overview open");
        Assert.NotNull(session.WaitForLine("OVERVIEW output=HEADLESS-1 open=true progress=1.00", 5000));
    }

    internal const string StepConfig = "[overview]\ngrid = \"never\"\nanimation_ms = 100\nwall = \"step\"\n";

    internal const string StepFourConfig =
        "[overview]\ngrid = \"never\"\nanimation_ms = 100\nsides = [\"left\", \"right\", \"top\", \"bottom\"]\nwall = \"step\"\n";

    internal static string TexturedStep(string keys) =>
        "[overview]\ngrid = \"never\"\nanimation_ms = 100\nwall = \"step\"\n" + keys;

    internal static Dictionary<string, string> OpenTextured(CanvasSession session)
    {
        session.Send("overview open");
        var line = session.WaitForLine("OVERVIEW output=HEADLESS-1 open=true progress=1.00", 5000);
        Assert.NotNull(line);
        return Fields(line!);
    }

    internal static double WallSpread(Shot shot)
    {
        var sum = 0.0;
        var squares = 0.0;
        var count = 0;
        for (var y = 200; y < 520; y++)
        {
            for (var x = 1124; x < 1150; x++)
            {
                var i = ((y * shot.Width) + x) * 4;
                var value = shot.Rgba[i] + shot.Rgba[i + 1] + shot.Rgba[i + 2];
                sum += value;
                squares += (double)value * value;
                count++;
            }
        }

        var mean = sum / count;
        return Math.Sqrt(Math.Max(0, (squares / count) - (mean * mean)));
    }

    internal static int BlueGridPixels(Shot shot)
    {
        var count = 0;
        for (var i = 0; i < shot.Rgba.Length; i += 4)
        {
            if (shot.Rgba[i + 2] > 150 && shot.Rgba[i] < 90 && shot.Rgba[i + 1] < 110)
            {
                count++;
            }
        }

        return count;
    }

    internal const string BareTerrace = "[canvas]\nenable = true\nwindow = \"terrace\"\ngrid = \"always\"\nanimation_ms = 100\n";

    internal static int GridPixelsIn(Shot shot, int x0, int y0, int x1, int y1, Box skip)
    {
        var count = 0;
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                if (x >= skip.X - 40 && x < skip.Right + 40 && y >= skip.Y - 40 && y < skip.Bottom + 40)
                {
                    continue;
                }

                var i = ((y * shot.Width) + x) * 4;
                if (shot.Rgba[i + 2] > 150 && shot.Rgba[i] < 90 && shot.Rgba[i + 1] < 110)
                {
                    count++;
                }
            }
        }

        return count;
    }

    internal static void AssertBarePlateau(CanvasSession session, string config, int x0, int y0, int x1, int y1, Action open)
    {
        open();
        var (_, _, window, _, _) = session.CanvasWindow();
        var bare = session.Shot("bare");
        var plateau = GridPixelsIn(bare, x0, y0, x1, y1, window);
        var shelf = GridPixelsIn(bare, 0, 0, 100, bare.Height, window);
        Assert.True(plateau < 200, $"{plateau} grid pixels are left on the desktop");
        Assert.True(shelf > 1000, $"the shelf drew {shelf} grid pixels");

        session.Rewrite(config.Replace("grid = \"always\"\n", "grid = \"always\"\ndesktop_grid = true\n", StringComparison.Ordinal));
        session.Send("reload");
        Assert.NotNull(session.WaitForLine("RELOAD "));
        open();
        var lined = GridPixelsIn(session.Shot("lined"), x0, y0, x1, y1, window);
        Assert.True(lined > 3000, $"desktop_grid = true drew {lined} grid pixels on the desktop");
    }

    internal static (int First, int Last) RedSpan(Shot shot, int y)
    {
        var first = -1;
        var last = -1;
        for (var x = 0; x < shot.Width; x++)
        {
            var i = ((y * shot.Width) + x) * 4;
            if (shot.Rgba[i] > 150 && shot.Rgba[i + 1] < 80 && shot.Rgba[i + 2] < 80)
            {
                first = first < 0 ? x : first;
                last = x;
            }
        }

        return (first, last);
    }

    internal static string? SsdWin([CallerFilePath] string sourcePath = "") => WlClient("ssdwin", sourcePath);

    internal static string? WlClient(string name, string sourcePath)
    {
        var tests = Path.GetDirectoryName(sourcePath);
        var root = tests is null ? null : Path.GetDirectoryName(Path.GetDirectoryName(tests));
        var candidate = root is null ? null : Path.Combine(root, "scripts", "wlclients", "bin", name);
        return candidate is not null && File.Exists(candidate) ? candidate : null;
    }

    internal static int Colored(Shot shot, int fromX, int width, bool red)
    {
        var count = 0;
        for (var y = 0; y < shot.Height; y++)
        {
            for (var x = fromX; x < fromX + width; x++)
            {
                var i = ((y * shot.Width) + x) * 4;
                var r = shot.Rgba[i];
                var g = shot.Rgba[i + 1];
                var b = shot.Rgba[i + 2];
                if (red ? r > 200 && g < 40 && b < 40 : b - r > 60 && b - g > 60)
                {
                    count++;
                }
            }
        }

        return count;
    }

    internal static int Saturated(Shot shot, int fromX, int width) => Saturated(shot, fromX, 0, width, shot.Height);

    internal static int Saturated(Shot shot, int fromX, int fromY, int width, int height)
    {
        var count = 0;
        for (var y = fromY; y < fromY + height; y++)
        {
            for (var x = fromX; x < fromX + width; x++)
            {
                var i = ((y * shot.Width) + x) * 4;
                var r = shot.Rgba[i];
                var g = shot.Rgba[i + 1];
                var b = shot.Rgba[i + 2];
                var max = Math.Max(r, Math.Max(g, b));
                var min = Math.Min(r, Math.Min(g, b));
                if (max - min > 100 && (r > 60 || g > 60))
                {
                    count++;
                }
            }
        }

        return count;
    }
}
