using Basin.UI.Avalonia;
using Xunit;

namespace Westonia.Tests;

public sealed class BootstrapTests
{
    [Fact]
    public void The_Avalonia_platform_starts_once_per_process()
    {
        Assert.False(BasinPlatform.IsStarted);

        BasinPlatform.ClaimStart();
        Assert.True(BasinPlatform.IsStarted);

        var refused = Assert.Throws<InvalidOperationException>(BasinPlatform.ClaimStart);
        Assert.Contains("process", refused.Message, StringComparison.Ordinal);
    }
}
