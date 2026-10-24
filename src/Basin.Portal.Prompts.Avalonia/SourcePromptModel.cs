using System.Collections.ObjectModel;
using Basin.Capabilities;

namespace Basin.Portal.Prompts.Avalonia;

/// <summary>
/// The view model of the screen share prompt, which asks which outputs or windows to share.
/// </summary>
public sealed class SourcePromptModel : PromptModel
{
    private readonly List<SourceRowModel> _selected = [];
    private bool _persist;

    /// <summary>
    /// Initializes a new instance of the <see cref="SourcePromptModel"/> class with a row for each output and window of the requested kinds.
    /// No row starts selected.
    /// </summary>
    /// <param name="prompt">The request that lists the sources and the kinds.</param>
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

    /// <summary>
    /// Gets the source rows, outputs first.
    /// </summary>
    public ObservableCollection<SourceRowModel> Rows { get; } = [];

    /// <summary>
    /// Gets a value indicating whether the user can select more than one source.
    /// </summary>
    public bool Multiple { get; }

    /// <summary>
    /// Gets a value indicating whether the dialog shows the choice to remember the answer.
    /// </summary>
    public bool OfferPersist { get; }

    /// <summary>
    /// Gets the label of the accept button, which is <c>Share</c>.
    /// </summary>
    public override string AcceptLabel => "Share";

    /// <summary>
    /// Gets the label of the deny button, which is <c>Cancel</c>.
    /// </summary>
    public override string DenyLabel => "Cancel";

    /// <summary>
    /// Gets or sets a value indicating whether the user asked to remember the choice. A change notifies the view.
    /// </summary>
    public bool Persist
    {
        get => _persist;
        set
        {
            _persist = value;
            Changed();
        }
    }

    /// <summary>
    /// Gets a new list of the selected sources, in the order of selection.
    /// </summary>
    public IReadOnlyList<SelectedSource> Selection => _selected.Select(r => r.Source).ToList();

    /// <summary>
    /// Gets a value indicating whether the accept command is allowed. It needs at least one selected source.
    /// </summary>
    protected override bool CanAccept => _selected.Count > 0;

    /// <summary>
    /// Tells whether a row is selected.
    /// </summary>
    /// <param name="row">The row to check.</param>
    /// <returns><see langword="true"/> when the row is selected.</returns>
    public bool IsSelected(SourceRowModel row) => _selected.Contains(row);

    /// <summary>
    /// Selects or deselects a row. With a single selection, the call first clears every other selection.
    /// </summary>
    /// <param name="row">The row to change.</param>
    /// <param name="selected"><see langword="true"/> to select the row.</param>
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
