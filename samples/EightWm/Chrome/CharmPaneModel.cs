namespace EightWm;

public sealed class CharmPaneModel : ObservableModel
{
    private string _title = string.Empty;
    private string _text = string.Empty;
    private bool _isSettings;

    public SettingsModel Settings { get; } = new();

    public string Title
    {
        get => _title;
        set => Set(ref _title, value);
    }

    public string Text
    {
        get => _text;
        set => Set(ref _text, value);
    }

    public bool IsSettings
    {
        get => _isSettings;
        set => Set(ref _isSettings, value);
    }
}
