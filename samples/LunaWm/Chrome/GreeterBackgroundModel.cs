using Avalonia.Media.Imaging;

namespace LunaWm;

public sealed class GreeterBackgroundModel : ObservableModel
{
    private string _clock = "";
    private string _date = "";
    private Bitmap? _wallpaper;
    private bool _highContrast;
    private bool _clockShown = true;

    public bool ClockShown
    {
        get => _clockShown;
        set
        {
            if (Set(ref _clockShown, value))
            {
                Raise(nameof(ClockOpacity));
            }
        }
    }

    public double ClockOpacity => _clockShown ? 1 : 0;

    public string Clock
    {
        get => _clock;
        set => Set(ref _clock, value);
    }

    public string Date
    {
        get => _date;
        set => Set(ref _date, value);
    }

    public Bitmap? Wallpaper
    {
        get => _wallpaper;
        set
        {
            if (Set(ref _wallpaper, value))
            {
                Raise(nameof(HasWallpaper));
            }
        }
    }

    public bool HasWallpaper => _wallpaper is not null && !_highContrast;

    public bool HighContrast
    {
        get => _highContrast;
        set
        {
            if (Set(ref _highContrast, value))
            {
                Raise(nameof(HasWallpaper));
            }
        }
    }
}
