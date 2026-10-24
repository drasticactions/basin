using Basin.Portal.Client;
using Xunit;

namespace Basin.Tests;

public sealed class RegionCaptureTests
{
    private static RegionPart[] Plan(Box box, Box[] outputs, double[] scales, double requested, Box crop = default)
    {
        var scale = RegionCapturePlan.TargetScale(box, outputs, scales, requested);
        var parts = new RegionPart[outputs.Length];
        var count = RegionCapturePlan.Plan(box, outputs, scales, scale, crop, parts);
        return parts[..count];
    }

    [Fact]
    public void A_box_inside_one_output_reads_that_part_of_the_output()
    {
        Box[] outputs = [new Box(0, 0, 1920, 1080)];
        double[] scales = [1];
        var box = new Box(100, 50, 300, 200);
        Assert.Equal(new Box(0, 0, 300, 200), RegionCapturePlan.Size(box, RegionCapturePlan.TargetScale(box, outputs, scales, 0)));
        var part = Assert.Single(Plan(box, outputs, scales, 0));
        Assert.Equal(new Box(0, 0, 300, 200), part.Destination);
        Assert.Equal((100.0, 50.0, 1.0), (part.SourceX, part.SourceY, part.Ratio));
    }

    [Fact]
    public void A_box_across_a_seam_takes_the_larger_scale_and_reads_both_outputs()
    {
        Box[] outputs = [new Box(0, 0, 1000, 800), new Box(1000, 0, 800, 600)];
        double[] scales = [1, 2];
        var box = new Box(900, 100, 200, 100);
        var scale = RegionCapturePlan.TargetScale(box, outputs, scales, 0);
        Assert.Equal(2, scale);
        Assert.Equal(new Box(0, 0, 400, 200), RegionCapturePlan.Size(box, scale));

        var parts = Plan(box, outputs, scales, 0);
        Assert.Equal(2, parts.Length);
        Assert.Equal((0, new Box(0, 0, 200, 200)), (parts[0].Output, parts[0].Destination));
        Assert.Equal((900.0, 100.0, 0.5), (parts[0].SourceX, parts[0].SourceY, parts[0].Ratio));
        Assert.Equal((1, new Box(200, 0, 200, 200)), (parts[1].Output, parts[1].Destination));
        Assert.Equal((0.0, 200.0, 1.0), (parts[1].SourceX, parts[1].SourceY, parts[1].Ratio));
    }

    [Fact]
    public void A_one_pixel_box_reads_the_pixel_under_it()
    {
        Box[] outputs = [new Box(0, 0, 1000, 800), new Box(1000, 0, 800, 600)];
        double[] scales = [1, 2];
        var part = Assert.Single(Plan(new Box(1300, 250, 1, 1), outputs, scales, 1));
        Assert.Equal((1, new Box(0, 0, 1, 1)), (part.Output, part.Destination));
        Assert.Equal((600.0, 500.0, 2.0), (part.SourceX, part.SourceY, part.Ratio));
    }

    [Fact]
    public void A_box_partly_outside_every_output_leaves_the_rest_unread()
    {
        Box[] outputs = [new Box(0, 0, 1000, 800)];
        double[] scales = [1];
        var box = new Box(900, 700, 300, 200);
        Assert.Equal(new Box(0, 0, 300, 200), RegionCapturePlan.Size(box, RegionCapturePlan.TargetScale(box, outputs, scales, 0)));
        var part = Assert.Single(Plan(box, outputs, scales, 0));
        Assert.Equal(new Box(0, 0, 100, 100), part.Destination);
        Assert.Equal((900.0, 700.0), (part.SourceX, part.SourceY));
        Assert.Empty(Plan(new Box(2000, 2000, 10, 10), outputs, scales, 0));
    }

    [Fact]
    public void A_crop_moves_the_parts_to_the_crop_origin()
    {
        Box[] outputs = [new Box(0, 0, 1000, 800)];
        double[] scales = [1];
        var part = Assert.Single(Plan(new Box(100, 100, 400, 300), outputs, scales, 0, new Box(50, 20, 10, 10)));
        Assert.Equal(new Box(0, 0, 10, 10), part.Destination);
        Assert.Equal((150.0, 120.0), (part.SourceX, part.SourceY));
    }
}
