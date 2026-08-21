using Xunit;

namespace Basin.Tests;

public sealed class DrmFormatTests
{
    [Theory]
    [InlineData(DrmFormat.Argb8888, DrmFormat.Xrgb8888)]
    [InlineData(DrmFormat.Abgr8888, DrmFormat.Xbgr8888)]
    [InlineData(DrmFormat.Argb2101010, DrmFormat.Xrgb2101010)]
    [InlineData(DrmFormat.Abgr2101010, DrmFormat.Xbgr2101010)]
    [InlineData(DrmFormat.Abgr16161616f, DrmFormat.Xbgr16161616f)]
    public void An_alpha_format_substitutes_the_matching_opaque_one(DrmFormat alpha, DrmFormat opaque)
    {
        Assert.True(alpha.HasAlpha());
        Assert.Equal(opaque, alpha.OpaqueSubstitute());
        Assert.False(opaque.HasAlpha());
        Assert.Equal(alpha.BytesPerPixel(), opaque.BytesPerPixel());
    }

    [Theory]
    [InlineData(DrmFormat.Xrgb8888)]
    [InlineData(DrmFormat.Xbgr2101010)]
    [InlineData(DrmFormat.Nv12)]
    [InlineData(DrmFormat.Rgb565)]
    public void A_format_without_alpha_is_its_own_substitute(DrmFormat format)
    {
        Assert.False(format.HasAlpha());
        Assert.Equal(format, format.OpaqueSubstitute());
    }
}
