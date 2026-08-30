namespace Basin;

public readonly record struct OutputProjection
{
    public OutputProjection(double scale, OutputTransform transform, int width, int height, int originX = 0, int originY = 0)
    {
        _scale = scale;
        Transform = transform;
        Width = width;
        Height = height;
        OriginX = originX;
        OriginY = originY;
    }

    public OutputProjection(double scale)
        : this(scale, OutputTransform.Normal, 0, 0)
    {
    }

    private readonly double _scale;

    public double Scale => _scale <= 0 ? 1.0 : _scale;

    public OutputTransform Transform { get; }

    private OutputTransform Rotation => Transform.Invert();

    public int Width { get; }

    public int Height { get; }

    public int OriginX { get; }

    public int OriginY { get; }

    public bool IsTransformed => Transform != OutputTransform.Normal;

    public bool MapsPixels => IsTransformed || OriginX != 0 || OriginY != 0;

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

    public static OutputProjection For(IOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var mode = output.CurrentMode;
        var transform = output.Transform;
        var content = output.ContentBox();
        return transform.SwapsAxes()
            ? new OutputProjection(output.Scale, transform, mode.Height, mode.Width, -content.X, -content.Y)
            : new OutputProjection(output.Scale, transform, mode.Width, mode.Height, -content.X, -content.Y);
    }

    public OutputProjection CroppedTo(int originX, int originY) =>
        new(Scale, Transform, Width, Height, originX, originY);

    public Box MapPixels(in Box pixels)
    {
        if (!MapsPixels)
        {
            return pixels;
        }

        var mapped = IsTransformed ? Rotation.Apply(pixels, Width, Height) : pixels;
        return mapped.Translated(-OriginX, -OriginY);
    }

    public Box Project(in Box logical) => MapPixels(OutputScaling.ToPhysical(logical, Scale));

    public Box ProjectExpanded(in Box logical) => MapPixels(OutputScaling.ToPhysicalExpanded(logical, Scale));

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
