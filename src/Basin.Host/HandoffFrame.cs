namespace Basin.Host;

public sealed class HandoffFrame
{
    internal HandoffFrame(string output, int outputWidth, int outputHeight, int frameWidth, int frameHeight)
    {
        Output = output;
        OutputWidth = outputWidth;
        OutputHeight = outputHeight;
        FrameWidth = frameWidth;
        FrameHeight = frameHeight;
        Destination = new Box(0, 0, outputWidth, outputHeight);
    }

    public string Output { get; }

    public int OutputWidth { get; }

    public int OutputHeight { get; }

    public int FrameWidth { get; }

    public int FrameHeight { get; }

    public float Opacity { get; set; } = 1f;

    public Box Destination { get; set; }

    public Box Clip { get; set; }
}
