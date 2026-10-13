using Avalonia.Media;

namespace EightWm;

public sealed class CurtainModel : ObservableModel
{
    private IImage? _picture;
    private IBrush _fill = Brushes.Black;
    private string _clock = "";
    private string _date = "";
    private string _network = "";
    private string _networkName = "";
    private string _batteryText = "";
    private bool _raised;
    private bool _dimmed;
    private bool _highContrast;

    public IImage? Picture
    {
        get => _picture;
        set => Set(ref _picture, value);
    }

    public IBrush Fill
    {
        get => _fill;
        set => Set(ref _fill, value);
    }

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

    public string NetworkGlyph
    {
        get => _network;
        set
        {
            if (Set(ref _network, value))
            {
                Raise(nameof(HasNetwork));
            }
        }
    }

    public string NetworkName
    {
        get => _networkName;
        set => Set(ref _networkName, value);
    }

    public bool HasNetwork => _network.Length > 0;

    public string BatteryText
    {
        get => _batteryText;
        set
        {
            if (Set(ref _batteryText, value))
            {
                Raise(nameof(HasBattery));
            }
        }
    }

    public bool HasBattery => _batteryText.Length > 0;

    public bool Raised
    {
        get => _raised;
        set => Set(ref _raised, value);
    }

    public bool Dimmed
    {
        get => _dimmed;
        set => Set(ref _dimmed, value);
    }

    public bool HighContrast
    {
        get => _highContrast;
        set => Set(ref _highContrast, value);
    }

    public event Action<string>? RaiseRequested;

    public event Action? NetworkRequested;

    public void RequestRaise(string how)
    {
        if (!_raised)
        {
            RaiseRequested?.Invoke(how);
        }
    }

    public void RequestNetwork() => NetworkRequested?.Invoke();
}
