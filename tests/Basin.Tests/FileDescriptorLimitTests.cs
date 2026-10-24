using Xunit;

namespace Basin.Tests;

public sealed class FileDescriptorLimitTests
{
    [Theory]
    [InlineData(1024UL, 524288UL, 4096UL)]
    [InlineData(524288UL, 524288UL, 524288UL)]
    [InlineData(1024UL, 2048UL, 2048UL)]
    [InlineData(8192UL, 65536UL, 8192UL)]
    [InlineData(1024UL, ulong.MaxValue, 4096UL)]
    public void The_soft_limit_rises_to_4096_and_never_falls(ulong current, ulong hard, ulong expected) =>
        Assert.Equal(expected, FileDescriptorLimit.SoftLimitFor(current, hard));
}
