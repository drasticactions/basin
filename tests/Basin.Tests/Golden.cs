using System.Runtime.CompilerServices;
using Basin.Diagnostics;

namespace Basin.Tests;

internal static class Golden
{
    public static void AssertMatches(CompositorTestHost host, string name, int gpuTolerance = 1, [CallerFilePath] string sourcePath = "")
    {
        var tolerance = host.Renderer is Basin.Render.Pixman.PixmanRenderer ? 0 : gpuTolerance;
        AssertMatches(host.Target, name, tolerance, sourcePath);
    }

    public static void AssertMatches(MemoryBuffer target, string name, int tolerance = 0, [CallerFilePath] string sourcePath = "")
    {
        var rgba = BufferCapture.ReadRgba(target);
        var goldenPath = Path.Combine(Path.GetDirectoryName(sourcePath)!, "Goldens", $"{name}.png");

        if (Environment.GetEnvironmentVariable("BASIN_UPDATE_GOLDENS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(goldenPath)!);
            File.WriteAllBytes(goldenPath, PngCodec.Encode(rgba, target.Width, target.Height));
            return;
        }

        if (!File.Exists(goldenPath))
        {
            throw new FileNotFoundException(
                $"Golden '{name}' missing. Run once with BASIN_UPDATE_GOLDENS=1 and commit {goldenPath}.");
        }

        var (expected, width, height) = PngCodec.Decode(File.ReadAllBytes(goldenPath));
        if (width != target.Width || height != target.Height)
        {
            throw new InvalidOperationException($"Golden '{name}' is {width}x{height}, target is {target.Width}x{target.Height}.");
        }

        var differing = 0;
        var maxDelta = 0;
        for (var i = 0; i < expected.Length; i += 4)
        {
            var pixelDelta = 0;
            for (var channel = 0; channel < 4; channel++)
            {
                pixelDelta = Math.Max(pixelDelta, Math.Abs(expected[i + channel] - rgba[i + channel]));
            }

            if (pixelDelta > tolerance)
            {
                differing++;
            }

            maxDelta = Math.Max(maxDelta, pixelDelta);
        }

        if (differing > 0)
        {
            var actualPath = Path.Combine(Path.GetTempPath(), $"basin-golden-{name}-actual.png");
            File.WriteAllBytes(actualPath, PngCodec.Encode(rgba, width, height));
            throw new InvalidOperationException(
                $"Golden '{name}' mismatch: {differing} pixels beyond ±{tolerance} (max channel delta {maxDelta}). Actual written to {actualPath}.");
        }
    }
}

internal static class Fill
{
    public static Action<nint, int> Solid(int width, int height, uint xrgb) => (data, stride) =>
    {
        unsafe
        {
            for (var y = 0; y < height; y++)
            {
                var row = (uint*)(data + y * stride);
                for (var x = 0; x < width; x++)
                {
                    row[x] = xrgb;
                }
            }
        }
    };

    public static Action<nint, int> Gradient(int width, int height) => (data, stride) =>
    {
        unsafe
        {
            for (var y = 0; y < height; y++)
            {
                var row = (uint*)(data + y * stride);
                for (var x = 0; x < width; x++)
                {
                    var r = (uint)(x * 255 / Math.Max(1, width - 1));
                    var g = (uint)(y * 255 / Math.Max(1, height - 1));
                    var b = (uint)((x ^ y) & 0xFF);
                    row[x] = 0xFF000000u | (r << 16) | (g << 8) | b;
                }
            }
        }
    };
}
