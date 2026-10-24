namespace Basin;

/// <summary>
/// Maps output-local logical coordinates to framebuffer pixels. It applies the scale, the inverse of the output transform and an optional crop origin.
/// </summary>
public readonly record struct OutputProjection
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OutputProjection"/> struct.
    /// </summary>
    /// <param name="scale">The logical-to-physical scale. A value of zero or less reads as 1.</param>
    /// <param name="transform">The output transform.</param>
    /// <param name="width">The width in physical pixels before the rotation, which is the mode width with the axes swapped for a quarter turn.</param>
    /// <param name="height">The height in physical pixels before the rotation.</param>
    /// <param name="originX">The horizontal crop origin in framebuffer pixels.</param>
    /// <param name="originY">The vertical crop origin in framebuffer pixels.</param>
    public OutputProjection(double scale, OutputTransform transform, int width, int height, int originX = 0, int originY = 0)
    {
        _scale = scale;
        Transform = transform;
        Width = width;
        Height = height;
        OriginX = originX;
        OriginY = originY;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="OutputProjection"/> struct with a scale only, no transform and no crop.
    /// </summary>
    /// <param name="scale">The logical-to-physical scale.</param>
    public OutputProjection(double scale)
        : this(scale, OutputTransform.Normal, 0, 0)
    {
    }

    private readonly double _scale;

    /// <summary>
    /// Gets the logical-to-physical scale. It is 1 when the projection was built with zero or a negative scale.
    /// </summary>
    public double Scale => _scale <= 0 ? 1.0 : _scale;

    /// <summary>
    /// Gets the output transform. The projection applies its inverse.
    /// </summary>
    public OutputTransform Transform { get; }

    private OutputTransform Rotation => Transform.Invert();

    /// <summary>
    /// Gets the width of the drawn area in physical pixels before the rotation.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// Gets the height of the drawn area in physical pixels before the rotation.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Gets the horizontal crop origin in framebuffer pixels, which the projection subtracts.
    /// </summary>
    public int OriginX { get; }

    /// <summary>
    /// Gets the vertical crop origin in framebuffer pixels, which the projection subtracts.
    /// </summary>
    public int OriginY { get; }

    /// <summary>
    /// Gets a value indicating whether the transform is not <see cref="OutputTransform.Normal"/>.
    /// </summary>
    public bool IsTransformed => Transform != OutputTransform.Normal;

    /// <summary>
    /// Gets a value indicating whether physical pixels need a map after the scale, which is when there is a transform or a crop origin.
    /// </summary>
    public bool MapsPixels => IsTransformed || OriginX != 0 || OriginY != 0;

    /// <summary>
    /// Gets the map from physical pixels to framebuffer pixels as an affine matrix, for geometry that is not an axis-aligned box.
    /// </summary>
    public RenderTransform Matrix
    {
        get
        {
            var transform = Rotation.ToMatrix(Width, Height);
            return OriginX == 0 && OriginY == 0
                ? transform
                : RenderTransform.Multiply(RenderTransform.Translation(-OriginX, -OriginY), transform);
        }
    }

    /// <summary>
    /// Builds the projection that an output presents with, from its current mode, scale and transform.
    /// </summary>
    /// <param name="output">The output to project for.</param>
    /// <returns>The projection without a crop origin.</returns>
    public static OutputProjection For(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var mode = output.CurrentMode;
        var transform = output.Transform;
        return transform.SwapsAxes()
            ? new OutputProjection(output.Scale, transform, mode.Height, mode.Width)
            : new OutputProjection(output.Scale, transform, mode.Width, mode.Height);
    }

    /// <summary>
    /// Makes a copy of the projection with another crop origin.
    /// </summary>
    /// <param name="originX">The horizontal crop origin in framebuffer pixels.</param>
    /// <param name="originY">The vertical crop origin in framebuffer pixels.</param>
    /// <returns>The cropped projection.</returns>
    public OutputProjection CroppedTo(int originX, int originY) =>
        new(Scale, Transform, Width, Height, originX, originY);

    /// <summary>
    /// Maps a box in physical pixels to framebuffer pixels through the rotation and the crop origin.
    /// </summary>
    /// <param name="pixels">The box in physical pixels before the rotation.</param>
    /// <returns>The box in framebuffer pixels. It is the same box when <see cref="MapsPixels"/> is <see langword="false"/>.</returns>
    public Box MapPixels(in Box pixels)
    {
        if (!MapsPixels)
        {
            return pixels;
        }

        var mapped = IsTransformed ? Rotation.Apply(pixels, Width, Height) : pixels;
        return mapped.Translated(-OriginX, -OriginY);
    }

    /// <summary>
    /// Maps a logical box to framebuffer pixels. Each edge rounds to the nearest pixel, so adjacent boxes stay adjacent.
    /// </summary>
    /// <param name="logical">The box in output-local logical coordinates.</param>
    /// <returns>The box in framebuffer pixels.</returns>
    public Box Project(in Box logical) => MapPixels(OutputScaling.ToPhysical(logical, Scale));

    /// <summary>
    /// Maps a logical box to framebuffer pixels and grows it to cover every pixel that the box touches, for damage.
    /// </summary>
    /// <param name="logical">The box in output-local logical coordinates.</param>
    /// <returns>The covering box in framebuffer pixels.</returns>
    public Box ProjectExpanded(in Box logical) => MapPixels(OutputScaling.ToPhysicalExpanded(logical, Scale));

    /// <summary>
    /// Maps a point in physical pixels to framebuffer pixels, rounded to the nearest pixel.
    /// </summary>
    /// <param name="x">The horizontal position in physical pixels.</param>
    /// <param name="y">The vertical position in physical pixels.</param>
    /// <returns>The point in framebuffer pixels.</returns>
    public (int X, int Y) MapPoint(int x, int y)
    {
        if (!MapsPixels)
        {
            return (x, y);
        }

        var (mx, my) = Matrix.Map(x, y);
        return ((int)Math.Round(mx), (int)Math.Round(my));
    }
}
