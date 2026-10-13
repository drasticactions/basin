namespace LunaWm;

public sealed class PassphraseRow : MenuRow
{
    private string _text = "";

    public string Text
    {
        get => _text;
        set => Set(ref _text, value);
    }

    public Action<string>? Submitted { get; init; }

    public Action? Cancelled { get; init; }

    public void Submit()
    {
        var text = _text;
        Text = "";
        Submitted?.Invoke(text);
    }
}
