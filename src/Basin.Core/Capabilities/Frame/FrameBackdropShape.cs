namespace Basin.Capabilities;

/// <summary>
/// The shape of a frame that a shaped backdrop effect, for example glass, draws behind the chrome. Lengths are in the
/// logical pixels of the frame surface, without outsets.
/// </summary>
public readonly record struct FrameBackdropShape
{
    /// <summary>
    /// Gets the width of the frame.
    /// </summary>
    public double Width { get; init; }

    /// <summary>
    /// Gets the height of the frame.
    /// </summary>
    public double Height { get; init; }

    /// <summary>
    /// Gets the radius of the outer corners.
    /// </summary>
    public double CornerRadius { get; init; }

    /// <summary>
    /// Gets the width of the bevel band along the outer edge.
    /// </summary>
    public double EdgeWidth { get; init; }

    /// <summary>
    /// Gets the client area inside the frame. It is empty when the window shows no client area, for example when it is shaded.
    /// </summary>
    public Box ClientHole { get; init; }

    /// <summary>
    /// Gets the corner radius of <see cref="ClientHole"/>.
    /// </summary>
    public double ClientRadius { get; init; }

    /// <summary>
    /// Gets the horizontal position of the highlight spot.
    /// </summary>
    public double HighlightX { get; init; }

    /// <summary>
    /// Gets the vertical position of the highlight spot.
    /// </summary>
    public double HighlightY { get; init; }

    /// <summary>
    /// Gets the strength of the highlight spot. 0 shows no highlight.
    /// </summary>
    public double HighlightStrength { get; init; }

    /// <summary>
    /// Gets how active the frame looks, from 0 for inactive to 1 for active. It can change gradually during a fade.
    /// </summary>
    public double Emphasis { get; init; }
}
