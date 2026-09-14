using System.ComponentModel;
using Basin.Config;

namespace Basin.Portal.Prompts.Avalonia;

public sealed class ShortcutRowModel : INotifyPropertyChanged
{
    private string _trigger;
    private bool _capturing;

    public ShortcutRowModel(string id, string description, string trigger, bool preferredTaken)
    {
        Id = id;
        Description = string.IsNullOrEmpty(description) ? id : description;
        _trigger = trigger;
        PreferredTaken = preferredTaken;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; }

    public string Description { get; }

    public bool PreferredTaken { get; }

    public string Trigger
    {
        get => _trigger;
        set
        {
            _trigger = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Trigger)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Display)));
        }
    }

    public bool Capturing
    {
        get => _capturing;
        set
        {
            _capturing = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Capturing)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Display)));
        }
    }

    public string Display => Capturing
        ? "press a chord…"
        : string.IsNullOrEmpty(Trigger)
            ? (PreferredTaken ? "taken" : "unbound")
            : TriggerSyntax.TryParse(Trigger, out var keysym, out var modifiers) ? TriggerSyntax.Describe(keysym, modifiers) : Trigger;
}
