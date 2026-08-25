namespace PlasmaHost.Shell;

public sealed class ShellCellModel : BreezeModel
{
    private double _x;
    private double _y;
    private double _width;
    private double _height;
    private string _title = string.Empty;
    private bool _selected;

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

    public string Title
    {
        get => _title;
        set => Set(ref _title, value);
    }

    public bool Selected
    {
        get => _selected;
        set => Set(ref _selected, value);
    }
}
