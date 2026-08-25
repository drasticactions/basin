using Avalonia.Media;

namespace PlasmaHost.Shell;

public sealed class DesktopOsdModel : BreezeModel
{
    private ShellBrushes _brushes = new(BreezePalette.Fallback);
    private string _name = string.Empty;
    private int _index;
    private int _count;

    public string Name
    {
        get => _name;
        set => Set(ref _name, value);
    }

    public int Index
    {
        get => _index;
        set
        {
            if (Set(ref _index, value))
            {
                Raise(nameof(Pips));
            }
        }
    }

    public int Count
    {
        get => _count;
        set
        {
            if (Set(ref _count, value))
            {
                Raise(nameof(Pips));
            }
        }
    }

    public IReadOnlyList<DesktopPip> Pips
    {
        get
        {
            var pips = new DesktopPip[_count];
            for (var i = 0; i < _count; i++)
            {
                pips[i] = new DesktopPip(i == _index ? Foreground : PipOff);
            }

            return pips;
        }
    }

    public ShellBrushes Brushes
    {
        get => _brushes;
        set
        {
            if (!Set(ref _brushes, value))
            {
                return;
            }

            Raise(nameof(Background));
            Raise(nameof(Foreground));
            Raise(nameof(PipOff));
            Raise(nameof(Pips));
        }
    }

    public IBrush Background => _brushes.OsdBackground;

    public IBrush Foreground => _brushes.Foreground;

    public IBrush PipOff => _brushes.PipOff;
}
