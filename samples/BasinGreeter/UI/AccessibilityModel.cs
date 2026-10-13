using System.Collections.ObjectModel;
using System.Windows.Input;

namespace BasinGreeter.UI;

public sealed class AccessibilityModel : Observable
{
    private bool _open;
    private bool _highContrast;
    private string _textSize = "100%";
    private bool _quiet;

    public AccessibilityModel()
    {
        Toggle = new RelayCommand(() => Open = !_open);
    }

    public ICommand Toggle { get; }

    public ObservableCollection<string> TextSizes { get; } = ["100%", "125%", "150%", "200%"];

    public event Action<bool>? HighContrastChanged;

    public event Action<bool>? ReaderToggled;

    private bool _readerAvailable;
    private bool _readerOn;

    public bool ReaderAvailable
    {
        get => _readerAvailable;
        set => Set(ref _readerAvailable, value);
    }

    public bool ReaderOn
    {
        get => _readerOn;
        set
        {
            if (Set(ref _readerOn, value) && !_quiet)
            {
                ReaderToggled?.Invoke(value);
            }
        }
    }

    public void ShowReader(bool on)
    {
        _quiet = true;
        ReaderOn = on;
        _quiet = false;
    }

    public event Action<double>? TextScaleChanged;

    public bool Open
    {
        get => _open;
        set => Set(ref _open, value);
    }

    public bool HighContrast
    {
        get => _highContrast;
        set
        {
            if (Set(ref _highContrast, value) && !_quiet)
            {
                HighContrastChanged?.Invoke(value);
            }
        }
    }

    public string TextSize
    {
        get => _textSize;
        set
        {
            if (Set(ref _textSize, value) && !_quiet)
            {
                TextScaleChanged?.Invoke(ScaleOf(value));
            }
        }
    }

    public static double ScaleOf(string size) =>
        double.TryParse(size.TrimEnd('%'), System.Globalization.CultureInfo.InvariantCulture, out var percent) ? percent / 100.0 : 1.0;

    public static string SizeOf(double scale) => $"{Math.Round(scale * 100).ToString(System.Globalization.CultureInfo.InvariantCulture)}%";

    public void Show(bool highContrast, double scale)
    {
        _quiet = true;
        HighContrast = highContrast;
        var size = SizeOf(scale);
        if (!TextSizes.Contains(size))
        {
            TextSizes.Add(size);
        }

        TextSize = size;
        _quiet = false;
    }
}
