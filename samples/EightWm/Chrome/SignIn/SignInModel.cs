using System.Collections.ObjectModel;
using System.Windows.Input;

namespace EightWm;

public sealed class SignInModel : ObservableModel
{
    private UserTileModel? _selected;
    private bool _showList;
    private bool _askName;
    private string _password = "";
    private string _prompt = "Password";
    private string _message = "";
    private string _error = "";
    private bool _busy;
    private bool _revealAllowed = true;
    private bool _revealed;
    private bool _optionsAvailable = true;
    private bool _optionsOpen;
    private bool _easeOpen;
    private bool _powerOpen;
    private bool _powerAvailable = true;
    private bool _highContrast;
    private bool _narrator;
    private bool _narratorAvailable;
    private bool _keyboard;
    private double _textScale = 1.0;
    private bool _secret = true;
    private bool _signingIn;

    public SignInModel()
    {
        Sessions.CollectionChanged += (_, _) => Raise(nameof(HasSessions));
        Layouts.CollectionChanged += (_, _) => Raise(nameof(HasLayouts));
        Submit = new RelayCommand(_ => DoSubmit());
        Back = new RelayCommand(_ =>
        {
            if (Users.Count > 1)
            {
                BackRequested?.Invoke();
            }
        });
        Choose = new RelayCommand(parameter =>
        {
            if (parameter is UserTileModel user)
            {
                UserChosen?.Invoke(user);
            }
        });
        ToggleOptions = new RelayCommand(_ => OptionsOpen = !_optionsOpen && _optionsAvailable);
        ToggleEase = new RelayCommand(_ => EaseOpen = !_easeOpen);
        TogglePower = new RelayCommand(_ => PowerOpen = !_powerOpen && _powerAvailable);
        Power = new RelayCommand(parameter =>
        {
            PowerOpen = false;
            if (parameter is string action)
            {
                PowerRequested?.Invoke(action);
            }
        });
        ChooseSession = new RelayCommand(parameter =>
        {
            if (parameter is SignInOption option)
            {
                SessionChosen?.Invoke(option.Id);
            }
        });
        ChooseLayout = new RelayCommand(parameter =>
        {
            if (parameter is SignInOption option)
            {
                LayoutChosen?.Invoke(option.Id);
            }
        });
        ChooseTextScale = new RelayCommand(parameter =>
        {
            if (parameter is string text && double.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var scale))
            {
                TextScaleChanged?.Invoke(scale);
            }
        });
    }

    public ObservableCollection<UserTileModel> Users { get; } = [];

    public ObservableCollection<SignInOption> Sessions { get; } = [];

    public ObservableCollection<SignInOption> Layouts { get; } = [];

    public bool HasSessions => Sessions.Count > 0;

    public bool HasLayouts => Layouts.Count > 0;

    public ICommand Submit { get; }

    public ICommand Back { get; }

    public ICommand Choose { get; }

    public ICommand ToggleOptions { get; }

    public ICommand ToggleEase { get; }

    public ICommand TogglePower { get; }

    public ICommand Power { get; }

    public ICommand ChooseSession { get; }

    public ICommand ChooseLayout { get; }

    public ICommand ChooseTextScale { get; }

    public event Action<string>? Submitted;

    public event Action? BackRequested;

    public event Action<UserTileModel>? UserChosen;

    public event Action<string>? SessionChosen;

    public event Action<string>? LayoutChosen;

    public event Action<string>? PowerRequested;

    public event Action<bool>? HighContrastChanged;

    public event Action<bool>? NarratorChanged;

    public event Action<bool>? KeyboardChanged;

    public event Action<double>? TextScaleChanged;

    public event Action? FocusPassword;

    public UserTileModel? SelectedUser
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value))
            {
                Raise(nameof(CurrentName));
            }
        }
    }

    public string CurrentName => _askName ? "Other user" : _selected?.DisplayName ?? "";

    public bool ShowList
    {
        get => _showList;
        set
        {
            if (Set(ref _showList, value))
            {
                Raise(nameof(ShowBox));
            }
        }
    }

    public bool ShowBox => !_showList;

    public bool AskName
    {
        get => _askName;
        set
        {
            if (Set(ref _askName, value))
            {
                Raise(nameof(CurrentName));
            }
        }
    }

    public bool CanGoBack => Users.Count > 1;

    public string Password
    {
        get => _password;
        set
        {
            if (Set(ref _password, value))
            {
                Raise(nameof(HasPassword));
            }
        }
    }

    public bool HasPassword => _password.Length > 0;

    public bool Secret
    {
        get => _secret;
        set => Set(ref _secret, value);
    }

    public string Prompt
    {
        get => _prompt;
        set => Set(ref _prompt, value);
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

    public string Error
    {
        get => _error;
        set
        {
            if (Set(ref _error, value))
            {
                Raise(nameof(HasError));
            }
        }
    }

    public bool HasError => _error.Length > 0;

    public bool Busy
    {
        get => _busy;
        set => Set(ref _busy, value);
    }

    public bool SigningIn
    {
        get => _signingIn;
        private set => Set(ref _signingIn, value);
    }

    public void ShowWelcome()
    {
        OptionsOpen = false;
        EaseOpen = false;
        PowerOpen = false;
        Busy = true;
        SigningIn = true;
    }

    public bool RevealAllowed
    {
        get => _revealAllowed;
        set
        {
            if (Set(ref _revealAllowed, value) && !value)
            {
                Revealed = false;
            }
        }
    }

    public bool Revealed
    {
        get => _revealed;
        set => Set(ref _revealed, value && _revealAllowed);
    }

    public bool OptionsAvailable
    {
        get => _optionsAvailable;
        set => Set(ref _optionsAvailable, value);
    }

    public bool OptionsOpen
    {
        get => _optionsOpen;
        set => Set(ref _optionsOpen, value);
    }

    public bool EaseOpen
    {
        get => _easeOpen;
        set => Set(ref _easeOpen, value);
    }

    public bool PowerOpen
    {
        get => _powerOpen;
        set => Set(ref _powerOpen, value);
    }

    public bool PowerAvailable
    {
        get => _powerAvailable;
        set => Set(ref _powerAvailable, value);
    }

    public bool HighContrast
    {
        get => _highContrast;
        set
        {
            if (Set(ref _highContrast, value))
            {
                HighContrastChanged?.Invoke(value);
            }
        }
    }

    public bool Narrator
    {
        get => _narrator;
        set
        {
            if (Set(ref _narrator, value))
            {
                NarratorChanged?.Invoke(value);
            }
        }
    }

    public bool NarratorAvailable
    {
        get => _narratorAvailable;
        set => Set(ref _narratorAvailable, value);
    }

    public bool OnScreenKeyboard
    {
        get => _keyboard;
        set
        {
            if (Set(ref _keyboard, value))
            {
                KeyboardChanged?.Invoke(value);
            }
        }
    }

    public double TextScale
    {
        get => _textScale;
        set => Set(ref _textScale, value);
    }

    public void ShowUsers(UserTileModel? selected)
    {
        AskName = false;
        SelectedUser = selected;
        ShowList = Users.Count > 1 && selected is null;
        Raise(nameof(CanGoBack));
    }

    public void BeginConversation(UserTileModel? user)
    {
        SelectedUser = user;
        ShowList = false;
        Message = "";
        Error = "";
        Password = "";
        Busy = false;
        Raise(nameof(CanGoBack));
        FocusPassword?.Invoke();
    }

    public void ShowPrompt(string prompt, bool secret)
    {
        Prompt = prompt.TrimEnd().TrimEnd(':');
        Secret = secret;
        Busy = false;
        FocusPassword?.Invoke();
    }

    public void ShowError(string error)
    {
        Error = error;
        Password = "";
        Busy = false;
        FocusPassword?.Invoke();
    }

    public void RequestFocus() => FocusPassword?.Invoke();

    public void SetHighContrastQuietly(bool on)
    {
        if (_highContrast != on)
        {
            _highContrast = on;
            Raise(nameof(HighContrast));
        }
    }

    public void SetNarratorQuietly(bool on)
    {
        if (_narrator != on)
        {
            _narrator = on;
            Raise(nameof(Narrator));
        }
    }

    public void SetKeyboardQuietly(bool on)
    {
        if (_keyboard != on)
        {
            _keyboard = on;
            Raise(nameof(OnScreenKeyboard));
        }
    }

    private void DoSubmit()
    {
        if (_busy)
        {
            return;
        }

        var text = _password;
        Password = "";
        Revealed = false;
        Error = "";
        Busy = true;
        Submitted?.Invoke(text);
    }
}
