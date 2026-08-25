namespace PlasmaHost.Shell;

public sealed class TileBoxModel : BreezeModel
{
    private double _x;
    private double _y;
    private double _width;
    private double _height;
    private bool _hot;

    public double X
    {
        get => _x;
        set => Set(ref _x, value);
    }

    public double Y
    {
        get => _y;
        set => Set(ref _y, value);
    }

    public double Width
    {
        get => _width;
        set => Set(ref _width, value);
    }

    public double Height
    {
        get => _height;
        set => Set(ref _height, value);
    }

    public bool Hot
    {
        get => _hot;
        set => Set(ref _hot, value);
    }
}
