using System.Runtime.CompilerServices;
using SkiaSharp;

namespace SdlComp.Tests;

internal static class Golden
{
    public static void AssertMatches(byte[] rgba, int width, int height, string name, int tolerance, [CallerFilePath] string sourcePath = "")
    {
        var path = Path.Combine(Path.GetDirectoryName(sourcePath)!, "Goldens", $"{name}.png");
        if (Environment.GetEnvironmentVariable("BASIN_UPDATE_GOLDENS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, Encode(rgba, width, height));
            return;
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Golden '{name}' missing. Run once with BASIN_UPDATE_GOLDENS=1 and commit {path}.");
        }

        using var expected = SKBitmap.Decode(path);
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var converted = new SKBitmap(info);
        if (expected.Width != width || expected.Height != height || !expected.CopyTo(converted, SKColorType.Rgba8888))
        {
            throw new InvalidOperationException($"Golden '{name}' is {expected.Width}x{expected.Height}, the capture is {width}x{height}.");
        }

        var want = converted.Bytes;
        var differing = 0;
        var maxDelta = 0;
        for (var i = 0; i < want.Length; i++)
        {
            var delta = Math.Abs(want[i] - rgba[i]);
            maxDelta = Math.Max(maxDelta, delta);
            if (delta > tolerance)
            {
                differing++;
            }
        }

        if (differing > 0)
        {
            var actual = Path.Combine(Path.GetTempPath(), $"sdl-golden-{name}-actual.png");
            File.WriteAllBytes(actual, Encode(rgba, width, height));
            throw new InvalidOperationException($"Golden '{name}' mismatch: {differing} channels beyond ±{tolerance} (max {maxDelta}). Actual written to {actual}.");
        }
    }

    private static byte[] Encode(byte[] rgba, int width, int height)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var image = SKImage.FromPixelCopy(info, rgba);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
