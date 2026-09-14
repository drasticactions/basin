using System.Collections.ObjectModel;
using Basin.Capabilities;

namespace Basin.Portal.Prompts.Avalonia;

public sealed class SourcePromptModel : PromptModel
{
    private readonly List<SourceRowModel> _selected = [];
    private bool _persist;

    public SourcePromptModel(in SourcePrompt prompt)
        : base("Share your screen?", prompt.AppId, prompt.DisplayName, prompt.IconPath)
    {
        Multiple = prompt.Multiple;
        OfferPersist = prompt.OfferPersist;
        if ((prompt.Kinds & PromptSourceKinds.Monitor) != 0)
        {
            foreach (var output in prompt.Outputs)
            {
                var mode = $"{output.LayoutBox.Width}×{output.LayoutBox.Height} at {output.LayoutBox.X},{output.LayoutBox.Y}";
                Rows.Add(new SourceRowModel(new SelectedSource(PromptSourceKinds.Monitor, output.Output, 0), output.Name, string.IsNullOrEmpty(output.Description) ? mode : $"{output.Description} — {mode}", "monitor"));
            }
        }

        if ((prompt.Kinds & PromptSourceKinds.Window) != 0)
        {
            foreach (var toplevel in prompt.Toplevels)
            {
                Rows.Add(new SourceRowModel(new SelectedSource(PromptSourceKinds.Window, null, toplevel.Id), string.IsNullOrEmpty(toplevel.Title) ? toplevel.AppId : toplevel.Title, toplevel.AppId, "window"));
            }
        }
    }

    public ObservableCollection<SourceRowModel> Rows { get; } = [];

    public bool Multiple { get; }

    public bool OfferPersist { get; }

    public override string AcceptLabel => "Share";

    public override string DenyLabel => "Cancel";

    public bool Persist
    {
        get => _persist;
        set
        {
            _persist = value;
            Changed();
        }
    }

    public IReadOnlyList<SelectedSource> Selection => _selected.Select(r => r.Source).ToList();

    protected override bool CanAccept => _selected.Count > 0;

    public bool IsSelected(SourceRowModel row) => _selected.Contains(row);

    public void SetSelected(SourceRowModel row, bool selected)
    {
        if (!Multiple)
        {
            _selected.Clear();
        }

        _selected.Remove(row);
        if (selected)
        {
            _selected.Add(row);
        }

        Changed(nameof(Selection));
    }
}
