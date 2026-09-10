using Basin.UI.Avalonia;
using Xunit;

namespace MauiComp.Tests;

public sealed class BootstrapTests
{
    [Fact]
    public void The_sample_pins_the_toolkit_pair_basin_pins()
    {
        var skia = typeof(SkiaSharp.SKObject).Assembly.GetName().Version;
        var native = SkiaSharp.SkiaSharpVersion.Native;

        Assert.NotNull(skia);
        Assert.True(native.Major >= 152, $"native libSkiaSharp is {native}, basin pins 152");
        Assert.False(BasinPlatform.IsStarted);
    }
}
