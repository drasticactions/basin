using System.ComponentModel;
using Basin.Config;

namespace Basin.Portal.Prompts.Avalonia;

/// <summary>
/// The view model of one row in the global shortcuts prompt.
/// </summary>
public sealed class ShortcutRowModel : INotifyPropertyChanged
{
    private string _trigger;
    private bool _capturing;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShortcutRowModel"/> class.
    /// </summary>
    /// <param name="id">The shortcut id.</param>
    /// <param name="description">The description of the shortcut. An empty description shows the id.</param>
    /// <param name="trigger">The trigger that the row starts with, or empty.</param>
    /// <param name="preferredTaken">A value indicating whether another binding holds the preferred trigger.</param>
    public ShortcutRowModel(string id, string description, string trigger, bool preferredTaken)
    {
        Id = id;
        Description = string.IsNullOrEmpty(description) ? id : description;
        _trigger = trigger;
        PreferredTaken = preferredTaken;
    }

    /// <summary>
    /// Occurs when <see cref="Trigger"/>, <see cref="Capturing"/> or <see cref="Display"/> changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Gets the shortcut id.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the text that names the shortcut. It is the id when the application gave no description.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets a value indicating whether another binding holds the preferred trigger.
    /// </summary>
    public bool PreferredTaken { get; }

    /// <summary>
    /// Gets or sets the trigger in the trigger syntax, or empty for no trigger.
    /// </summary>
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

    /// <summary>
    /// Gets or sets a value indicating whether the row waits for a chord.
    /// </summary>
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

    /// <summary>
    /// Gets the text for the trigger column: a capture hint, <c>taken</c>, <c>unbound</c> or the readable form of the trigger.
    /// </summary>
    public string Display => Capturing
        ? "press a chord…"
        : string.IsNullOrEmpty(Trigger)
            ? (PreferredTaken ? "taken" : "unbound")
            : TriggerSyntax.TryParse(Trigger, out var keysym, out var modifiers) ? TriggerSyntax.Describe(keysym, modifiers) : Trigger;
}
