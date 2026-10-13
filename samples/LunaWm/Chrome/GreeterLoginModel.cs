using System.Collections.ObjectModel;
using System.Windows.Input;

namespace LunaWm;

public sealed class GreeterLoginModel : ObservableModel
{
    private bool _showUsers = true;
    private bool _askName;
    private string _nameText = "";
    private string _currentUser = "";
    private string _promptText = "";
    private bool _hasPrompt;
    private bool _promptSecret = true;
    private string _fieldText = "";
    private string _errorText = "";
    private bool _needsDismiss;
    private bool _busy;
    private bool _signingIn;
    private int _shakes;
    private GreeterUserEntry? _selectedUser;
    private bool _keyboardVisible;
    private double _keyboardHeight;
    private double _keyboardKeysHeight;
    private bool _highContrast;
    private string _sessionName = "";

    public GreeterLoginModel()
    {
        Choose = new RelayCommand(parameter =>
        {
            if (!_signingIn && parameter is GreeterUserEntry user && (_showUsers || !ReferenceEquals(user, _selectedUser)))
            {
                SelectedUser = user;
                UserChosen?.Invoke(user);
            }
        });
        Submit = new RelayCommand(_ => OnSubmit());
        Cancel = new RelayCommand(_ => Cancelled?.Invoke());
        Dismiss = new RelayCommand(_ => Dismissed?.Invoke());
        PickSession = new RelayCommand(_ => SessionPickRequested?.Invoke());
    }

    public ObservableCollection<GreeterUserEntry> Users { get; } = [];

    public ObservableCollection<string> Messages { get; } = [];

    public ICommand Choose { get; }

    public ICommand Submit { get; }

    public ICommand Cancel { get; }

    public ICommand Dismiss { get; }

    public ICommand PickSession { get; }

    public event Action<GreeterUserEntry>? UserChosen;

    public event Action<string>? NameEntered;

    public event Action<string>? Answered;

    public event Action? Cancelled;

    public event Action? Dismissed;

    public event Action? FocusRequested;

    public event Action? PromptShown;

    public event Action? SessionPickRequested;

    public string SessionName
    {
        get => _sessionName;
        set
        {
            if (Set(ref _sessionName, value))
            {
                Raise(nameof(HasSession));
                Raise(nameof(ShowStripSession));
            }
        }
    }

    public bool HasSession => _sessionName.Length > 0;

    public bool ShowStripSession => _showUsers && HasSession;

    public bool ShowUsers
    {
        get => _showUsers;
        private set
        {
            if (Set(ref _showUsers, value))
            {
                Raise(nameof(ShowPad));
                Raise(nameof(ShowStripSession));
            }
        }
    }

    public bool ShowPad => !_showUsers;

    public bool SigningIn
    {
        get => _signingIn;
        private set
        {
            if (Set(ref _signingIn, value))
            {
                Raise(nameof(ContentOpacity));
            }
        }
    }

    public double ContentOpacity => _signingIn ? 0 : 1;

    public bool AskName
    {
        get => _askName;
        private set
        {
            if (Set(ref _askName, value))
            {
                Raise(nameof(ShowCurrentUser));
            }
        }
    }

    public bool ShowCurrentUser => !_askName;

    public string NameText
    {
        get => _nameText;
        set => Set(ref _nameText, value);
    }

    public string CurrentUser
    {
        get => _currentUser;
        private set => Set(ref _currentUser, value);
    }

    public string PromptText
    {
        get => _promptText;
        private set => Set(ref _promptText, value);
    }

    public bool HasPrompt
    {
        get => _hasPrompt;
        private set
        {
            if (Set(ref _hasPrompt, value))
            {
                Raise(nameof(SecretShown));
                Raise(nameof(VisibleShown));
            }
        }
    }

    public bool PromptSecret
    {
        get => _promptSecret;
        private set
        {
            if (Set(ref _promptSecret, value))
            {
                Raise(nameof(SecretShown));
                Raise(nameof(VisibleShown));
            }
        }
    }

    public bool SecretShown => _hasPrompt && _promptSecret;

    public bool VisibleShown => _hasPrompt && !_promptSecret;

    public string FieldText
    {
        get => _fieldText;
        set => Set(ref _fieldText, value);
    }

    public string ErrorText
    {
        get => _errorText;
        private set
        {
            if (Set(ref _errorText, value))
            {
                Raise(nameof(HasError));
            }
        }
    }

    public bool HasError => _errorText.Length > 0;

    public bool NeedsDismiss
    {
        get => _needsDismiss;
        private set
        {
            if (Set(ref _needsDismiss, value))
            {
                Raise(nameof(CanSubmit));
                Raise(nameof(ShowDone));
            }
        }
    }

    public bool ShowDone => !_needsDismiss;

    public bool Busy
    {
        get => _busy;
        set
        {
            if (Set(ref _busy, value))
            {
                Raise(nameof(CanSubmit));
            }
        }
    }

    public bool CanSubmit => !_busy && !_needsDismiss;

    public int Shakes
    {
        get => _shakes;
        private set => Set(ref _shakes, value);
    }

    public GreeterUserEntry? SelectedUser
    {
        get => _selectedUser;
        set
        {
            if (ReferenceEquals(_selectedUser, value))
            {
                return;
            }

            if (_selectedUser is not null)
            {
                _selectedUser.Selected = false;
            }

            Set(ref _selectedUser, value);
            if (value is not null)
            {
                value.Selected = true;
            }
        }
    }

    public bool KeyboardVisible
    {
        get => _keyboardVisible;
        set
        {
            if (Set(ref _keyboardVisible, value))
            {
                Raise(nameof(PadMargin));
            }
        }
    }

    public double KeyboardHeight
    {
        get => _keyboardHeight;
        set => Set(ref _keyboardHeight, value);
    }

    public double KeyboardKeysHeight
    {
        get => _keyboardKeysHeight;
        set
        {
            if (Set(ref _keyboardKeysHeight, value))
            {
                Raise(nameof(PadMargin));
            }
        }
    }

    public Avalonia.Thickness PadMargin => new(0, 0, 0, _keyboardVisible ? 12 + _keyboardKeysHeight : 40);

    public bool HighContrast
    {
        get => _highContrast;
        set => Set(ref _highContrast, value);
    }

    public bool HasTextField => !_showUsers && (_askName || _hasPrompt);

    public void ShowUserStrip(GreeterUserEntry? selected)
    {
        ShowUsers = true;
        AskName = false;
        ClearConversation();
        SelectedUser = selected;
        FocusRequested?.Invoke();
    }

    public void AskForName()
    {
        ShowUsers = false;
        AskName = true;
        CurrentUser = "";
        NameText = "";
        ClearConversation();
        FocusRequested?.Invoke();
        PromptShown?.Invoke();
    }

    public void ShowSigningIn()
    {
        KeyboardVisible = false;
        Busy = true;
        SigningIn = true;
    }

    public bool CanSwitchInPlace => !_showUsers && !_askName && _hasPrompt && _promptSecret && !_busy && !_needsDismiss;

    public void SwitchUser(string user)
    {
        CurrentUser = user;
        Messages.Clear();
        Raise(nameof(HasMessages));
        FieldText = "";
        ErrorText = "";
    }

    public void BeginConversation(string user)
    {
        ShowUsers = false;
        AskName = false;
        CurrentUser = user;
        ClearConversation();
        Busy = true;
    }

    public void ShowPrompt(string text, bool secret)
    {
        PromptText = text.TrimEnd();
        PromptSecret = secret;
        FieldText = "";
        HasPrompt = true;
        Busy = false;
        FocusRequested?.Invoke();
        PromptShown?.Invoke();
    }

    public bool HasMessages => Messages.Count > 0;

    public void AddMessage(string text)
    {
        Messages.Add(text.TrimEnd());
        Raise(nameof(HasMessages));
    }

    public void ShowError(string text, bool needsDismiss, bool shake)
    {
        ErrorText = text;
        NeedsDismiss = needsDismiss;
        Busy = false;
        if (needsDismiss)
        {
            HasPrompt = false;
            FocusRequested?.Invoke();
        }

        if (shake)
        {
            Shakes++;
        }
    }

    public void ClearError()
    {
        ErrorText = "";
        NeedsDismiss = false;
    }

    private void ClearConversation()
    {
        Messages.Clear();
        Raise(nameof(HasMessages));
        PromptText = "";
        HasPrompt = false;
        FieldText = "";
        ErrorText = "";
        NeedsDismiss = false;
        Busy = false;
    }

    private void OnSubmit()
    {
        if (_showUsers)
        {
            if (_selectedUser is { } user)
            {
                UserChosen?.Invoke(user);
            }

            return;
        }

        if (_needsDismiss)
        {
            Dismissed?.Invoke();
            return;
        }

        if (_askName)
        {
            if (_nameText.Trim() is { Length: > 0 } name)
            {
                NameEntered?.Invoke(name);
            }

            return;
        }

        if (_hasPrompt && !_busy)
        {
            var text = _fieldText;
            FieldText = "";
            HasPrompt = false;
            Busy = true;
            Answered?.Invoke(text);
        }
    }
}
