namespace Basin.Host;

/// <summary>
/// Holds the state of an inherited frame on one output during a handoff transition. An <see cref="IHandoffTransition"/> changes <see cref="Opacity"/>, <see cref="Destination"/> and <see cref="Clip"/>, and the driver applies them to the scene once per frame.
/// </summary>
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

    /// <summary>
    /// Gets the name of the output that shows the frame.
    /// </summary>
    public string Output { get; }

    /// <summary>
    /// Gets the output width in logical pixels.
    /// </summary>
    public int OutputWidth { get; }

    /// <summary>
    /// Gets the output height in logical pixels.
    /// </summary>
    public int OutputHeight { get; }

    /// <summary>
    /// Gets the width in pixels of the part of the inherited buffer that the old compositor showed.
    /// </summary>
    public int FrameWidth { get; }

    /// <summary>
    /// Gets the height in pixels of the part of the inherited buffer that the old compositor showed.
    /// </summary>
    public int FrameHeight { get; }

    /// <summary>
    /// Gets or sets the opacity of the frame, from 0 to 1. At 0 the driver also disables the frame's node.
    /// </summary>
    public float Opacity { get; set; } = 1f;

    /// <summary>
    /// Gets or sets the rectangle that the frame is drawn into, in output coordinates. It starts as the whole output.
    /// </summary>
    public Box Destination { get; set; }

    /// <summary>
    /// Gets or sets the clip rectangle in output coordinates. An empty box means no clip.
    /// </summary>
    public Box Clip { get; set; }
}
