using System.Collections.ObjectModel;
using System.Windows.Input;

namespace BasinGreeter.UI;

public sealed class LoginModel : Observable
{
    private bool _showList = true;
    private bool _askUsername;
    private string _usernameText = "";
    private string _currentUser = "";
    private string _promptText = "";
    private bool _hasPrompt;
    private bool _promptIsSecret = true;
    private string _fieldText = "";
    private string _errorText = "";
    private bool _needsDismiss;
    private bool _busy;
    private SessionEntry? _selectedSession;
    private UserEntry? _selectedUser;
    private bool _allowCommand;
    private string _commandText = "";

    public LoginModel()
    {
        Submit = new RelayCommand(OnSubmit);
        Cancel = new RelayCommand(() => Cancelled?.Invoke());
        Dismiss = new RelayCommand(() => Dismissed?.Invoke());
        ChooseSelected = new RelayCommand(() =>
        {
            if (_selectedUser is { } user)
            {
                UserChosen?.Invoke(user);
            }
        });
    }

    public BarModel Bar { get; } = new();

    public AccessibilityModel Accessibility { get; } = new();

    private bool _keyboardVisible;
    private bool _keyboardAllowed;
    private double _keyboardHeight;

    public ICommand ToggleKeyboard => _toggleKeyboard ??= new RelayCommand(() => KeyboardToggled?.Invoke());

    private ICommand? _toggleKeyboard;

    public event Action? KeyboardToggled;

    public bool KeyboardVisible
    {
        get => _keyboardVisible;
        set => Set(ref _keyboardVisible, value);
    }

    public bool KeyboardAllowed
    {
        get => _keyboardAllowed;
        set => Set(ref _keyboardAllowed, value);
    }

    public double KeyboardHeight
    {
        get => _keyboardHeight;
        set => Set(ref _keyboardHeight, value);
    }

    public bool HasTextField => (!_showList && (_askUsername || _hasPrompt)) || Bar.Network.Asking;

    public ObservableCollection<UserEntry> Users { get; } = [];

    public ObservableCollection<SessionEntry> Sessions { get; } = [];

    public ObservableCollection<string> Messages { get; } = [];

    public ICommand Submit { get; }

    public ICommand Cancel { get; }

    public ICommand Dismiss { get; }

    public ICommand ChooseSelected { get; }

    public event Action<UserEntry>? UserChosen;

    public event Action<string>? UsernameEntered;

    public event Action<string>? Answered;

    public event Action? Cancelled;

    public event Action? Dismissed;

    public event Action? FocusRequested;

    public event Action? PromptShown;

    public event Action<SessionEntry?>? SessionChosen;

    public bool ShowList
    {
        get => _showList;
        private set
        {
            if (Set(ref _showList, value))
            {
                Raise(nameof(ShowConversation));
            }
        }
    }

    public bool ShowConversation => !_showList;

    public bool AskUsername
    {
        get => _askUsername;
        private set => Set(ref _askUsername, value);
    }

    public string UsernameText
    {
        get => _usernameText;
        set => Set(ref _usernameText, value);
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
        private set => Set(ref _hasPrompt, value);
    }

    public bool PromptIsSecret
    {
        get => _promptIsSecret;
        private set
        {
            if (Set(ref _promptIsSecret, value))
            {
                Raise(nameof(MaskChar));
            }
        }
    }

    public char MaskChar => _promptIsSecret ? '●' : '\0';

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
            }
        }
    }

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

    public UserEntry? SelectedUser
    {
        get => _selectedUser;
        set => Set(ref _selectedUser, value);
    }

    public SessionEntry? SelectedSession
    {
        get => _selectedSession;
        set
        {
            if (Set(ref _selectedSession, value))
            {
                SessionChosen?.Invoke(value);
            }
        }
    }

    public bool AllowCommand
    {
        get => _allowCommand;
        set => Set(ref _allowCommand, value);
    }

    public string CommandText
    {
        get => _commandText;
        set => Set(ref _commandText, value);
    }

    public void ShowUsers(UserEntry? selected)
    {
        ShowList = true;
        AskUsername = false;
        ClearConversation();
        SelectedUser = selected ?? (Users.Count > 0 ? Users[0] : null);
        FocusRequested?.Invoke();
    }

    public void AskForUsername()
    {
        ShowList = false;
        AskUsername = true;
        CurrentUser = "";
        UsernameText = "";
        ClearConversation();
        FocusRequested?.Invoke();
        PromptShown?.Invoke();
    }

    public void BeginConversation(string user)
    {
        ShowList = false;
        AskUsername = false;
        CurrentUser = user;
        ClearConversation();
        Busy = true;
    }

    public void ShowPrompt(string text, bool secret)
    {
        PromptText = text.TrimEnd();
        PromptIsSecret = secret;
        FieldText = "";
        HasPrompt = true;
        Busy = false;
        FocusRequested?.Invoke();
        PromptShown?.Invoke();
    }

    public void AddMessage(string text) => Messages.Add(text.TrimEnd());

    public void ShowError(string text, bool needsDismiss)
    {
        ErrorText = text;
        NeedsDismiss = needsDismiss;
        Busy = false;
        if (needsDismiss)
        {
            HasPrompt = false;
        }
    }

    public void ClearError()
    {
        ErrorText = "";
        NeedsDismiss = false;
    }

    public string TakeField()
    {
        var text = _fieldText;
        FieldText = "";
        HasPrompt = false;
        Busy = true;
        return text;
    }

    private void ClearConversation()
    {
        Messages.Clear();
        PromptText = "";
        HasPrompt = false;
        FieldText = "";
        ErrorText = "";
        NeedsDismiss = false;
        Busy = false;
    }

    private void OnSubmit()
    {
        if (_showList)
        {
            ChooseSelected.Execute(null);
            return;
        }

        if (_needsDismiss)
        {
            Dismissed?.Invoke();
            return;
        }

        if (_askUsername)
        {
            if (_usernameText.Trim() is { Length: > 0 } name)
            {
                UsernameEntered?.Invoke(name);
            }

            return;
        }

        if (_hasPrompt && !_busy)
        {
            Answered?.Invoke(TakeField());
        }
    }
}
