namespace PlasmaHost.Shell;

public sealed class ShellDesktopModel : BreezeModel
{
    private double _x;
    private double _y;
    private double _width;
    private double _height;
    private string _name = string.Empty;
    private bool _current;
    private bool _highlighted;

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

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    public bool IsCurrent
    {
        get => _current;
        set => Set(ref _current, value);
    }

    public bool Highlighted
    {
        get => _highlighted;
        set => Set(ref _highlighted, value);
    }
}
