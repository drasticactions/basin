using System.Collections.ObjectModel;
using Basin.Capabilities;
using Basin.Config;

namespace Basin.Portal.Prompts.Avalonia;

public sealed class ShortcutPromptModel : PromptModel
{
    public ShortcutPromptModel(in ShortcutPrompt prompt)
        : base("Bind shortcuts", prompt.AppId, prompt.DisplayName, prompt.IconPath)
    {
        foreach (var row in prompt.Shortcuts)
        {
            var initial = !string.IsNullOrEmpty(row.CurrentTrigger) ? row.CurrentTrigger : row.PreferredTaken ? "" : row.PreferredTrigger;
            Rows.Add(new ShortcutRowModel(row.Id, row.Description, initial, row.PreferredTaken));
        }
    }

    public ObservableCollection<ShortcutRowModel> Rows { get; } = [];

    public override string AcceptLabel => "Bind";

    public override string DenyLabel => "Cancel";

    public ShortcutRowModel? Capturing => Rows.FirstOrDefault(r => r.Capturing);

    public IReadOnlyList<ShortcutBinding> Bindings =>
        Rows.Where(r => !string.IsNullOrEmpty(r.Trigger)).Select(r => new ShortcutBinding(r.Id, r.Trigger)).ToList();

    public void ToggleCapture(ShortcutRowModel row)
    {
        var was = row.Capturing;
        foreach (var other in Rows)
        {
            other.Capturing = false;
        }

        row.Capturing = !was;
    }

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
