using System.Windows.Input;

namespace TabWm.Chrome;

public sealed class SettingsViewModel : ObservableModel
{
    private int _section;
    private int _variant;
    private int _colorScheme;
    private int _engine;
    private bool _closeWhenEmpty;
    private bool _highContrast;
    private bool _reducedMotion;
    private decimal _closeTimeoutSeconds = 5;
    private bool _restore = true;
    private string _terminal = string.Empty;
    private string _lockCommand = string.Empty;
    private bool _xwayland = true;
    private string _status = string.Empty;
    private bool _loading;

    public SettingsViewModel()
    {
        CloseCommand = new RelayCommand(_ => Dismiss?.Invoke());
    }

    public static IReadOnlyList<string> Variants { get; } = ["Follow the color scheme", "Light", "Dark"];

    public static IReadOnlyList<string> Engines { get; } = ["Automatic", "Native (Qt)", "Managed"];

    public IReadOnlyList<string> ColorSchemes { get; set; } = ["Session default"];

    public IReadOnlyList<SettingsKeyModel> Keys { get; set; } = [];

    public string ConfigPath { get; set; } = string.Empty;

    public bool CanSave { get; set; } = true;

    public string Version { get; set; } = string.Empty;

    public string BasinVersion { get; set; } = string.Empty;

    public string Backend { get; set; } = string.Empty;

    public string Renderer { get; set; } = string.Empty;

    public string Engine { get; set; } = string.Empty;

    public ICommand CloseCommand { get; }

    public Action<string, object>? Changed { get; set; }

    public Action? Dismiss { get; set; }

    public int Section
    {
        get => _section;
        set => Set(ref _section, value);
    }

    public int Variant
    {
        get => _variant;
        set => Update(ref _variant, value);
    }

    public int ColorScheme
    {
        get => _colorScheme;
        set => Update(ref _colorScheme, value);
    }

    public int EngineChoice
    {
        get => _engine;
        set => Update(ref _engine, value);
    }

    public bool CloseWhenEmpty
    {
        get => _closeWhenEmpty;
        set => Update(ref _closeWhenEmpty, value);
    }

    public decimal CloseTimeoutSeconds
    {
        get => _closeTimeoutSeconds;
        set => Update(ref _closeTimeoutSeconds, value);
    }

    public bool HighContrast
    {
        get => _highContrast;
        set => Update(ref _highContrast, value);
    }

    public bool ReducedMotion
    {
        get => _reducedMotion;
        set => Update(ref _reducedMotion, value);
    }

    public bool Restore
    {
        get => _restore;
        set => Update(ref _restore, value);
    }

    public string Terminal
    {
        get => _terminal;
        set => Update(ref _terminal, value);
    }

    public string LockCommand
    {
        get => _lockCommand;
        set => Update(ref _lockCommand, value);
    }

    public bool XWayland
    {
        get => _xwayland;
        set => Update(ref _xwayland, value);
    }

    public string Status
    {
        get => _status;
        set
        {
            if (Set(ref _status, value))
            {
                Raise(nameof(HasStatus));
            }
        }
    }

    public bool HasStatus => _status.Length > 0;

    public void Load(Action fill)
    {
        _loading = true;
        try
        {
            fill();
        }
        finally
        {
            _loading = false;
        }
    }

    private void Update<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (Set(ref field, value, name) && !_loading && name is not null)
        {
            Changed?.Invoke(name, value!);
        }
    }
}
