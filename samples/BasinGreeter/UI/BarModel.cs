using System.Windows.Input;

namespace BasinGreeter.UI;

public sealed class BarModel : Observable
{
    private bool _batteryVisible;
    private string _batteryText = "";
    private bool _networkVisible;
    private string _networkText = "";
    private string _networkGlyph = "GlyphOffline";
    private bool _powerVisible;
    private bool _canSuspend;
    private bool _canRestart;
    private bool _canShutDown;
    private string _confirmText = "";
    private string _confirmAction = "";

    public BarModel()
    {
        Suspend = new RelayCommand(() => PowerRequested?.Invoke("suspend"));
        Restart = new RelayCommand(() => Ask("restart", GreeterText.ConfirmRestart));
        ShutDown = new RelayCommand(() => Ask("shutdown", GreeterText.ConfirmShutDown));
        Confirm = new RelayCommand(() =>
        {
            var action = _confirmAction;
            ConfirmText = "";
            _confirmAction = "";
            PowerRequested?.Invoke(action);
        });
        CancelConfirm = new RelayCommand(() =>
        {
            ConfirmText = "";
            _confirmAction = "";
        });
    }

    public event Action<string>? PowerRequested;

    public NetworkModel Network { get; } = new();

    public System.Collections.ObjectModel.ObservableCollection<string> Layouts { get; } = [];

    public event Action<string>? LayoutChosen;

    private string? _selectedLayout;
    private bool _settingLayout;

    public bool LayoutVisible => Layouts.Count > 1;

    public string? SelectedLayout
    {
        get => _selectedLayout;
        set
        {
            if (Set(ref _selectedLayout, value) && value is not null && !_settingLayout)
            {
                LayoutChosen?.Invoke(value);
            }
        }
    }

    public void ShowLayouts(IEnumerable<string> layouts)
    {
        Layouts.Clear();
        foreach (var layout in layouts)
        {
            Layouts.Add(layout);
        }

        Raise(nameof(LayoutVisible));
    }

    public void ShowLayout(string? layout)
    {
        _settingLayout = true;
        SelectedLayout = layout;
        _settingLayout = false;
    }

    public ICommand Suspend { get; }

    public ICommand Restart { get; }

    public ICommand ShutDown { get; }

    public ICommand Confirm { get; }

    public ICommand CancelConfirm { get; }

    public bool BatteryVisible
    {
        get => _batteryVisible;
        set => Set(ref _batteryVisible, value);
    }

    public string BatteryText
    {
        get => _batteryText;
        set => Set(ref _batteryText, value);
    }

    public bool NetworkVisible
    {
        get => _networkVisible;
        set => Set(ref _networkVisible, value);
    }

    public string NetworkText
    {
        get => _networkText;
        set => Set(ref _networkText, value);
    }

    public string NetworkGlyph
    {
        get => _networkGlyph;
        set => Set(ref _networkGlyph, value);
    }

    public bool PowerVisible
    {
        get => _powerVisible;
        set => Set(ref _powerVisible, value);
    }

    public bool CanSuspend
    {
        get => _canSuspend;
        set => Set(ref _canSuspend, value);
    }

    public bool CanRestart
    {
        get => _canRestart;
        set => Set(ref _canRestart, value);
    }

    public bool CanShutDown
    {
        get => _canShutDown;
        set => Set(ref _canShutDown, value);
    }

    public string ConfirmText
    {
        get => _confirmText;
        private set
        {
            if (Set(ref _confirmText, value))
            {
                Raise(nameof(Confirming));
            }
        }
    }

    public bool Confirming => _confirmText.Length > 0;

    public string ConfirmAction => _confirmAction;

    public void Ask(string action, string text)
    {
        _confirmAction = action;
        ConfirmText = text;
    }
}
