namespace Basin.Frames.MsStyles;

/// <summary>
/// The values that one call to <see cref="MsStylesDrawing.Background"/> needs: the image cache, the output DPI and an opacity.
/// </summary>
public readonly struct MsStylesDrawContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MsStylesDrawContext"/> struct.
    /// </summary>
    /// <param name="images">The cache that decodes the theme's images.</param>
    /// <param name="dpi">The output DPI, 96 times the output scale.</param>
    /// <param name="opacity">The opacity from 0 to 1 for the image operations.</param>
    public MsStylesDrawContext(MsStylesImageCache images, double dpi, float opacity = 1f)
    {
        Images = images;
        Dpi = dpi;
        Opacity = opacity;
    }

    /// <summary>
    /// Gets the cache that decodes the theme's images.
    /// </summary>
    public MsStylesImageCache Images { get; }

    /// <summary>
    /// Gets the output DPI. It selects the image for a part whose <c>IMAGESELECTTYPE</c> is DPI.
    /// </summary>
    public double Dpi { get; }

    /// <summary>
    /// Gets the output scale, which is <see cref="Dpi"/> divided by 96.
    /// </summary>
    public double Scale => Dpi / 96.0;

    /// <summary>
    /// Gets the opacity from 0 to 1 for the image operations.
    /// </summary>
    public float Opacity { get; }
}
