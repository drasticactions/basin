using Basin.Capabilities;
using Xunit;

namespace Basin.Tests;

public sealed class CursorImagesTests
{
    private const int PlaneWidth = 64;
    private const int PlaneHeight = 64;

    private static CursorImages Create(IAllocator allocator, int logicalSize = 24) =>
        new(allocator, PlaneWidth, PlaneHeight, logicalSize: logicalSize);

    [Fact]
    public void Size_is_the_exact_product_of_the_logical_size_and_the_scale()
    {
        using var allocator = new ShmAllocator();
        using var cursors = Create(allocator);

        Assert.Equal(24, cursors.SizeForScale(1));
        Assert.Equal(48, cursors.SizeForScale(2));

        Assert.Equal(36, cursors.SizeForScale(1.5));
        Assert.Equal(30, cursors.SizeForScale(1.25));

        Assert.Equal(32, cursors.SizeForScale(1.3333));
        Assert.Equal(32, cursors.SizeForScale(4.0 / 3.0));

        Assert.Equal(12, cursors.SizeForScale(0.5));
        Assert.Equal(1, cursors.SizeForScale(0));
    }

    [Fact]
    public void Construction_loads_at_the_density_it_was_given()
    {
        using var allocator = new ShmAllocator();
        using var unscaled = new CursorImages(allocator, PlaneWidth, PlaneHeight);
        using var scaled = new CursorImages(allocator, PlaneWidth, PlaneHeight, logicalSize: 24, scale: 2);

        Assert.Equal(24, unscaled.Size);
        Assert.Equal(48, scaled.Size);

        Assert.False(scaled.ReloadForScale(2, () => Assert.Fail("reacquired without a density change")));
    }

    [Fact]
    public void Images_are_allocated_at_the_plane_size()
    {
        using var allocator = new ShmAllocator();
        using var cursors = Create(allocator);
        Assert.SkipWhen(!cursors.HasTheme, "no xcursor theme installed");

        var image = cursors.Named("left_ptr");
        Assert.SkipWhen(image is null, "theme has no left_ptr or its aliases");
        Assert.Equal(PlaneWidth, image!.Value.Buffer.Width);
        Assert.Equal(PlaneHeight, image.Value.Buffer.Height);
    }

    [Fact]
    public void A_cursor_too_large_for_the_buffer_is_scaled_onto_the_plane()
    {
        using var allocator = new ShmAllocator();
        using var cursors = new CursorImages(allocator, 24, 24, logicalSize: 24);
        Assert.SkipWhen(!cursors.HasTheme, "no xcursor theme installed");

        var fits = cursors.Named("left_ptr", new CursorKey(1, null));
        Assert.SkipWhen(fits is null, "theme has no left_ptr or its aliases");

        var theme = XcursorTheme.Load(null, cursors.SizeForScale(4));
        Assert.SkipWhen(theme is null, "no xcursor theme installed");
        var cursor = theme!.Get("left_ptr");
        foreach (var alias in CursorAliases.Of("left_ptr"))
        {
            cursor ??= theme.Get(alias);
        }

        Assert.SkipWhen(cursor is null, "theme has no left_ptr or its aliases");
        var frame = cursor!.Frame(0);
        Assert.SkipWhen(frame.Width <= 24 && frame.Height <= 24, "theme carries no frame larger than the buffer");

        var oversized = cursors.Named("left_ptr", new CursorKey(4, null));
        Assert.NotNull(oversized);
        Assert.False(oversized!.Value.Clipped);
        Assert.Equal(24, oversized.Value.Buffer.Width);
        Assert.Equal(24, oversized.Value.Buffer.Height);

        var factor = Math.Min(24.0 / frame.Width, 24.0 / frame.Height);
        Assert.Equal(Math.Max(1, (int)Math.Round(frame.Width * factor)), oversized.Value.Width);
        Assert.Equal(Math.Max(1, (int)Math.Round(frame.Height * factor)), oversized.Value.Height);
        Assert.Equal((int)Math.Round(frame.HotspotX * factor), oversized.Value.HotspotX);
        Assert.Equal((int)Math.Round(frame.HotspotY * factor), oversized.Value.HotspotY);
    }

    [Fact]
    public void A_name_resolves_through_its_aliases()
    {
        Assert.Contains("default", CursorAliases.Of("left_ptr"));
        Assert.Contains("ew-resize", CursorAliases.Of("sb_h_double_arrow"));
        Assert.Empty(CursorAliases.Of("no_such_cursor"));
    }

    [Fact]
    public void Lookups_are_cached_including_the_misses()
    {
        using var allocator = new CountingAllocator();
        using var cursors = Create(allocator);
        Assert.SkipWhen(!cursors.HasTheme, "no xcursor theme installed");

        var first = cursors.Named("left_ptr");
        Assert.SkipWhen(first is null, "theme has no left_ptr or its aliases");
        var allocations = allocator.Count;

        Assert.Equal(first!.Value.Buffer, cursors.Named("left_ptr")!.Value.Buffer);
        Assert.Equal(allocations, allocator.Count);

        Assert.Null(cursors.Named("basin_no_such_cursor"));
        Assert.Null(cursors.Named("basin_no_such_cursor"));
        Assert.Equal(allocations, allocator.Count);
    }

    [Fact]
    public void A_second_scale_becomes_a_second_variant_and_the_first_survives()
    {
        using var allocator = new ShmAllocator();
        using var cursors = Create(allocator);
        Assert.SkipWhen(!cursors.HasTheme, "no xcursor theme installed");

        var before = cursors.Named("left_ptr");
        Assert.SkipWhen(before is null, "theme has no left_ptr or its aliases");
        Assert.Equal(1, cursors.VariantCount);

        IBuffer? onThePlane = null;
        Assert.True(cursors.ReloadForScale(2, () => onThePlane = cursors.Named("left_ptr")?.Buffer));

        Assert.Equal(48, cursors.Size);
        Assert.Equal(2, cursors.VariantCount);
        Assert.NotNull(onThePlane);
        Assert.NotEqual(before!.Value.Buffer, onThePlane);

        Assert.False(onThePlane!.IsDestroyed);
        Assert.False(before.Value.Buffer.IsDestroyed);
    }

    [Fact]
    public void A_scale_that_comes_back_reuses_its_variant()
    {
        using var allocator = new ShmAllocator();
        using var cursors = Create(allocator);
        Assert.SkipWhen(!cursors.HasTheme, "no xcursor theme installed");

        var first = cursors.Named("left_ptr", new CursorKey(1, null));
        Assert.SkipWhen(first is null, "theme has no left_ptr or its aliases");

        cursors.Named("left_ptr", new CursorKey(2, null));
        var again = cursors.Named("left_ptr", new CursorKey(1, null));

        Assert.Equal(2, cursors.VariantCount);
        Assert.Equal(first!.Value.Buffer, again!.Value.Buffer);
        Assert.Equal(24, cursors.Size);
    }

    [Fact]
    public void A_scale_off_the_grid_snaps_onto_an_existing_variant()
    {
        using var allocator = new ShmAllocator();
        using var cursors = Create(allocator);

        cursors.Named("left_ptr", new CursorKey(1.5, null));
        var afterFirst = cursors.VariantCount;

        cursors.Named("left_ptr", new CursorKey(1.5000001, null));
        Assert.Equal(afterFirst, cursors.VariantCount);

        cursors.Named("left_ptr", new CursorKey(1.6, null));
        Assert.Equal(afterFirst + 1, cursors.VariantCount);
    }

    [Fact]
    public void Reload_at_the_same_density_changes_nothing()
    {
        using var allocator = new ShmAllocator();
        using var cursors = Create(allocator);
        Assert.SkipWhen(!cursors.HasTheme, "no xcursor theme installed");

        var reacquired = false;
        Assert.False(cursors.ReloadForScale(1, () => reacquired = true));
        Assert.False(reacquired);
    }

    [Fact]
    public void A_client_surface_is_resampled_to_the_density_it_is_shown_at()
    {
        using var allocator = new ShmAllocator();
        using var cursors = Create(allocator);

        var source = new MemoryBuffer(2, 2, DrmFormat.Argb8888);
        try
        {
            Assert.True(source.BeginDataAccess(BufferDataAccess.Write, out var view));
            unsafe
            {
                for (var y = 0; y < 2; y++)
                {
                    var row = (uint*)(view.Data + (y * view.Stride));
                    row[0] = y == 0 ? 0xFFFFFFFF : 0x00000000;
                    row[1] = 0x00000000;
                }
            }

            source.EndDataAccess();

            var image = cursors.FromSurface(source, 1, 1, scale: 2);
            Assert.NotNull(image);

            Assert.Equal(2, image!.Value.HotspotX);
            Assert.Equal(2, image.Value.HotspotY);

            var rgba = Basin.Diagnostics.BufferCapture.ReadRgba(image.Value.Buffer);
            byte Alpha(int x, int y) => rgba[(((y * PlaneWidth) + x) * 4) + 3];

            Assert.Equal(0xFF, Alpha(0, 0));
            Assert.True(Alpha(1, 1) is > 0x40 and < 0xFF);
            Assert.Equal(0x00, Alpha(3, 3));
            Assert.Equal(0x00, Alpha(4, 4));
        }
        finally
        {
            source.Destroy();
        }
    }

    [Fact]
    public void A_client_surface_becomes_a_plane_sized_image()
    {
        using var allocator = new ShmAllocator();
        using var cursors = Create(allocator);

        var source = new MemoryBuffer(16, 16, DrmFormat.Argb8888);
        try
        {
            Assert.True(source.BeginDataAccess(BufferDataAccess.Write, out var view));
            unsafe
            {
                for (var y = 0; y < 16; y++)
                {
                    var row = (uint*)(view.Data + (y * view.Stride));
                    for (var x = 0; x < 16; x++)
                    {
                        row[x] = 0xFF00FF00;
                    }
                }
            }

            source.EndDataAccess();

            var image = cursors.FromSurface(source, 4, 5);
            Assert.NotNull(image);
            Assert.Equal(4, image!.Value.HotspotX);
            Assert.Equal(5, image.Value.HotspotY);
            Assert.Equal(PlaneWidth, image.Value.Buffer.Width);

            var rgba = Basin.Diagnostics.BufferCapture.ReadRgba(image.Value.Buffer);
            Assert.Equal(0xFF, rgba[((0 * PlaneWidth) + 0) * 4 + 3]);
            Assert.Equal(0x00, rgba[((32 * PlaneWidth) + 32) * 4 + 3]);

            Assert.Equal(image.Value.Buffer, cursors.FromSurface(source, 0, 0)!.Value.Buffer);
        }
        finally
        {
            source.Destroy();
        }
    }

    [Fact]
    public void An_identity_transform_survives_the_premultiplied_round_trip()
    {
        using var allocator = new ShmAllocator();
        using var cursors = Create(allocator);
        cursors.ColorProfiles = new StubProfiles(Identity());
        UseDescribed(cursors);

        var image = UploadClient(cursors, 0x80402010);
        var raw = ReadArgb(image, 0, 0);

        Assert.Equal(0x80u, (raw >> 24) & 0xFF);
        Assert.Equal(0x40u, (raw >> 16) & 0xFF);
        Assert.Equal(0x20u, (raw >> 8) & 0xFF);
        Assert.Equal(0x10u, raw & 0xFF);
    }

    [Fact]
    public void A_transform_reaches_the_cursor_pixels_and_leaves_transparency_alone()
    {
        using var allocator = new ShmAllocator();
        using var cursors = Create(allocator);
        cursors.ColorProfiles = new StubProfiles(SwapRedAndBlue());
        UseDescribed(cursors);

        var image = UploadClient(cursors, 0xFFFF0000);
        Assert.Equal(0xFF0000FFu, ReadArgb(image, 0, 0));

        var clear = UploadClient(cursors, 0x00FF0000);
        Assert.Equal(0x00FF0000u, ReadArgb(clear, 0, 0));
    }

    [Fact]
    public void With_no_colour_service_the_cursor_uploads_unconverted()
    {
        using var allocator = new ShmAllocator();
        using var cursors = Create(allocator);
        UseDescribed(cursors);

        var image = UploadClient(cursors, 0xFFFF0000);
        Assert.Equal(0xFFFF0000u, ReadArgb(image, 0, 0));
    }

    [Fact]
    public void A_second_description_becomes_a_second_variant()
    {
        using var allocator = new ShmAllocator();
        using var cursors = Create(allocator);
        cursors.ColorProfiles = new StubProfiles(Identity());

        var wide = new ImageDescription { PrimariesNamed = ColorPrimaries.Bt2020 };
        var before = cursors.VariantCount;

        cursors.Named("left_ptr", new CursorKey(1, wide));
        Assert.Equal(before + 1, cursors.VariantCount);

        cursors.Named("left_ptr", new CursorKey(1, new ImageDescription { PrimariesNamed = ColorPrimaries.Bt2020 }));
        Assert.Equal(before + 1, cursors.VariantCount);
    }

    private static void UseDescribed(CursorImages cursors) =>
        cursors.Named("left_ptr", new CursorKey(1, new ImageDescription { PrimariesNamed = ColorPrimaries.Bt2020 }));

    private static CursorImage UploadClient(CursorImages cursors, uint argb)
    {
        var source = new MemoryBuffer(1, 1, DrmFormat.Argb8888);
        try
        {
            Assert.True(source.BeginDataAccess(BufferDataAccess.Write, out var view));
            unsafe
            {
                *(uint*)view.Data = argb;
            }

            source.EndDataAccess();
            var image = cursors.FromSurface(source, 0, 0);
            Assert.NotNull(image);
            return image!.Value;
        }
        finally
        {
            source.Destroy();
        }
    }

    private static unsafe uint ReadArgb(CursorImage image, int x, int y)
    {
        Assert.True(image.Buffer.BeginDataAccess(BufferDataAccess.Read, out var view));
        try
        {
            return *(uint*)(view.Data + (y * view.Stride) + (x * 4));
        }
        finally
        {
            image.Buffer.EndDataAccess();
        }
    }

    private static ColorLut3D Identity(int size = 33)
    {
        var data = new float[size * size * size * 3];
        var index = 0;
        for (var b = 0; b < size; b++)
        {
            for (var g = 0; g < size; g++)
            {
                for (var r = 0; r < size; r++)
                {
                    data[index++] = r / (float)(size - 1);
                    data[index++] = g / (float)(size - 1);
                    data[index++] = b / (float)(size - 1);
                }
            }
        }

        return new ColorLut3D(size, data);
    }

    private static ColorLut3D SwapRedAndBlue(int size = 33)
    {
        var data = new float[size * size * size * 3];
        var index = 0;
        for (var b = 0; b < size; b++)
        {
            for (var g = 0; g < size; g++)
            {
                for (var r = 0; r < size; r++)
                {
                    data[index++] = b / (float)(size - 1);
                    data[index++] = g / (float)(size - 1);
                    data[index++] = r / (float)(size - 1);
                }
            }
        }

        return new ColorLut3D(size, data);
    }

    private sealed class StubProfiles(ColorLut3D lut) : IColorProfileService
    {
        public ColorFeatures Features => ColorFeatures.Transforms;

        public bool TryParseIcc(ReadOnlySpan<byte> profile, out ImageDescription description)
        {
            description = ImageDescription.SdrDefault;
            return false;
        }

        public bool TryBuildParametric(ImageDescription parameters, out ImageDescription description)
        {
            description = ImageDescription.SdrDefault;
            return false;
        }

        public IColorLut? BuildTransform(
            ImageDescription source, ImageDescription output, IRenderer renderer, ColorRenderIntent intent) => null;

        public ColorLut3D? BuildLut(ImageDescription source, ImageDescription output, ColorRenderIntent intent) => lut;

        public bool TryDescribeOutput(IOutput output, out ImageDescription description)
        {
            description = ImageDescription.SdrDefault;
            return false;
        }
    }

    private sealed class CountingAllocator : IAllocator
    {
        private readonly ShmAllocator _inner = new();

        public int Count { get; private set; }

        public DrmFormatSet Formats => _inner.Formats;

        public IBuffer? Allocate(int width, int height, DrmFormat format, ReadOnlySpan<ulong> modifiers, BufferUse use)
        {
            Count++;
            return _inner.Allocate(width, height, format, modifiers, use);
        }

        public void Dispose() => _inner.Dispose();
    }
}
