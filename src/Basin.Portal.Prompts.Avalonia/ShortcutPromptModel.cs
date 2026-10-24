using System.Collections.ObjectModel;
using Basin.Capabilities;
using Basin.Config;

namespace Basin.Portal.Prompts.Avalonia;

/// <summary>
/// The view model of the global shortcuts prompt, which binds a key chord to each requested shortcut.
/// At most one row captures a chord at a time.
/// </summary>
public sealed class ShortcutPromptModel : PromptModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ShortcutPromptModel"/> class.
    /// Each row starts with its current trigger, or with the preferred trigger when no other binding holds it.
    /// </summary>
    /// <param name="prompt">The request that lists the shortcuts.</param>
    public ShortcutPromptModel(in ShortcutPrompt prompt)
        : base("Bind shortcuts", prompt.AppId, prompt.DisplayName, prompt.IconPath)
    {
        foreach (var row in prompt.Shortcuts)
        {
            var initial = !string.IsNullOrEmpty(row.CurrentTrigger) ? row.CurrentTrigger : row.PreferredTaken ? "" : row.PreferredTrigger;
            Rows.Add(new ShortcutRowModel(row.Id, row.Description, initial, row.PreferredTaken));
        }
    }

    /// <summary>
    /// Gets one row for each requested shortcut.
    /// </summary>
    public ObservableCollection<ShortcutRowModel> Rows { get; } = [];

    /// <summary>
    /// Gets the label of the accept button, which is <c>Bind</c>.
    /// </summary>
    public override string AcceptLabel => "Bind";

    /// <summary>
    /// Gets the label of the deny button, which is <c>Cancel</c>.
    /// </summary>
    public override string DenyLabel => "Cancel";

    /// <summary>
    /// Gets the row that waits for a chord, or <see langword="null"/>.
    /// </summary>
    public ShortcutRowModel? Capturing => Rows.FirstOrDefault(r => r.Capturing);

    /// <summary>
    /// Gets a new list with the trigger of each row that has one. Rows without a trigger are left out.
    /// </summary>
    public IReadOnlyList<ShortcutBinding> Bindings =>
        Rows.Where(r => !string.IsNullOrEmpty(r.Trigger)).Select(r => new ShortcutBinding(r.Id, r.Trigger)).ToList();

    /// <summary>
    /// Starts capture on a row and stops it on every other row. A row that captures already stops.
    /// </summary>
    /// <param name="row">The row to toggle.</param>
    public void ToggleCapture(ShortcutRowModel row)
    {
        var was = row.Capturing;
        foreach (var other in Rows)
        {
            other.Capturing = false;
        }

        row.Capturing = !was;
    }

    /// <summary>
    /// Sets the trigger of the capturing row from a keysym name and modifiers, and stops capture.
    /// </summary>
    /// <param name="keysymName">The XKB keysym name, for example <c>a</c>.</param>
    /// <param name="modifiers">The modifiers that are held.</param>
    /// <returns><see langword="false"/> when no row captures or the name is not a keysym.</returns>
    public bool Capture(string keysymName, Modifiers modifiers)
    {
        if (Capturing is not { } row)
        {
            return false;
        }

        var keysym = Keysym.FromName(keysymName);
        if (keysym == Keysym.NoSymbol)
        {
            return false;
        }

        row.Trigger = TriggerSyntax.Format(keysym, modifiers);
        row.Capturing = false;
        return true;
    }

    /// <summary>
    /// Clears the trigger of the capturing row and stops capture.
    /// </summary>
    /// <returns><see langword="false"/> when no row captures.</returns>
    public bool Clear()
    {
        if (Capturing is not { } row)
        {
            return false;
        }

        row.Trigger = "";
        row.Capturing = false;
        return true;
    }
}
