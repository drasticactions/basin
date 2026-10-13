using System.Collections.ObjectModel;
using System.Windows.Input;

namespace BasinGreeter.UI;

public sealed class NetworkModel : Observable
{
    private bool _open;
    private bool _canJoin = true;
    private bool _wifiEnabled;
    private bool _hasWifi;
    private string _askingFor = "";
    private string _passphrase = "";
    private string _message = "";
    private bool _settingWifi;

    public NetworkModel()
    {
        Toggle = new RelayCommand(() =>
        {
            if (_canJoin)
            {
                Open = !_open;
            }
        });
        Connect = new RelayCommand(() =>
        {
            if (_askingFor.Length > 0)
            {
                var text = _passphrase;
                Passphrase = "";
                PassphraseEntered?.Invoke(_askingFor, text);
            }
        });
        CancelPassphrase = new RelayCommand(() => AskingFor = "");
    }

    public ObservableCollection<NetworkPointEntry> Points { get; } = [];

    public ICommand Toggle { get; }

    public ICommand Connect { get; }

    public ICommand CancelPassphrase { get; }

    public event Action<bool>? WifiToggled;

    public event Action<string, string>? PassphraseEntered;

    public event Action? FocusPassphrase;

    public bool Open
    {
        get => _open;
        set
        {
            if (Set(ref _open, value) && !value)
            {
                AskingFor = "";
            }
        }
    }

    public bool CanJoin
    {
        get => _canJoin;
        set => Set(ref _canJoin, value);
    }

    public bool HasWifi
    {
        get => _hasWifi;
        set => Set(ref _hasWifi, value);
    }

    public bool WifiEnabled
    {
        get => _wifiEnabled;
        set
        {
            if (Set(ref _wifiEnabled, value) && !_settingWifi)
            {
                WifiToggled?.Invoke(value);
            }
        }
    }

    public string AskingFor
    {
        get => _askingFor;
        set
        {
            if (Set(ref _askingFor, value))
            {
                Raise(nameof(Asking));
                Passphrase = "";
                if (value.Length > 0)
                {
                    FocusPassphrase?.Invoke();
                }
            }
        }
    }

    public bool Asking => _askingFor.Length > 0;

    public string Passphrase
    {
        get => _passphrase;
        set => Set(ref _passphrase, value);
    }

    public string Message
    {
        get => _message;
        set
        {
            if (Set(ref _message, value))
            {
                Raise(nameof(HasMessage));
            }
        }
    }

    public bool HasMessage => _message.Length > 0;

    public void ShowWifi(bool enabled)
    {
        _settingWifi = true;
        WifiEnabled = enabled;
        _settingWifi = false;
    }
}
