using Basin.Capabilities;
using SkiaSharp;

namespace Basin.Portal.Prompts.Skia;

/// <summary>
/// Asks the user which outputs or windows to share. It shows up to 8 rows at a time and scrolls the rest.
/// The first row starts selected.
/// </summary>
public sealed class SourcePromptView : SkiaPromptView
{
    private const int MaxRows = 8;

    private readonly List<Row> _rows = [];
    private readonly bool _multiple;
    private readonly bool _offerPersist;
    private int _hotRow = -1;
    private int _scroll;

    /// <summary>
    /// Initializes a new instance of the <see cref="SourcePromptView"/> class with a row for each output and window of the requested kinds.
    /// </summary>
    /// <param name="theme">The theme to draw with.</param>
    /// <param name="prompt">The request that lists the sources and the kinds.</param>
    public SourcePromptView(SkiaPromptTheme theme, in SourcePrompt prompt)
        : base(theme, "Share your screen?", SkiaPortalPrompts.AppLine(prompt.AppId, prompt.DisplayName), prompt.IconPath)
    {
        _multiple = prompt.Multiple;
        _offerPersist = prompt.OfferPersist;
        if ((prompt.Kinds & PromptSourceKinds.Monitor) != 0)
        {
            foreach (var output in prompt.Outputs)
            {
                var mode = $"{output.LayoutBox.Width}×{output.LayoutBox.Height} at {output.LayoutBox.X},{output.LayoutBox.Y}";
                _rows.Add(new Row(new SelectedSource(PromptSourceKinds.Monitor, output.Output, 0), output.Name, string.IsNullOrEmpty(output.Description) ? mode : $"{output.Description} — {mode}"));
            }
        }

        if ((prompt.Kinds & PromptSourceKinds.Window) != 0)
        {
            foreach (var toplevel in prompt.Toplevels)
            {
                _rows.Add(new Row(new SelectedSource(PromptSourceKinds.Window, null, toplevel.Id), string.IsNullOrEmpty(toplevel.Title) ? toplevel.AppId : toplevel.Title, toplevel.AppId));
            }
        }

        if (_rows.Count > 0)
        {
            _rows[0].Selected = true;
        }

        AddButton("Cancel", PromptResponse.Denied, primary: false);
        AddButton("Share", PromptResponse.Accepted, primary: true);
    }

    /// <summary>
    /// Gets a value indicating whether the user asked to remember the choice.
    /// </summary>
    public bool Persist { get; private set; }

    /// <summary>
    /// Gets a new list of the selected sources, in row order.
    /// </summary>
    public IReadOnlyList<SelectedSource> Selection => _rows.Where(r => r.Selected).Select(r => r.Source).ToList();

    /// <summary>
    /// Gets the number of source rows.
    /// </summary>
    public int RowCount => _rows.Count;

    private int VisibleRows => Math.Min(_rows.Count, MaxRows);

    /// <summary>
    /// Gets the height of the dialog in logical pixels, from the visible rows and the persist row.
    /// </summary>
    public override int Height => BodyTop + (Math.Max(1, VisibleRows) * RowHeight) + (_offerPersist ? RowHeight : 0) + 16 + ButtonHeight + Padding + 12;

    /// <summary>
    /// Allows the Share button only when a source is selected.
    /// </summary>
    /// <returns><see langword="true"/> when a row is selected.</returns>
    protected override bool CanAccept() => _rows.Any(r => r.Selected);

    /// <summary>
    /// Paints the visible source rows and the persist row.
    /// </summary>
    /// <param name="canvas">The canvas to draw on.</param>
    /// <param name="width">The surface width in logical pixels.</param>
    /// <param name="height">The surface height in logical pixels.</param>
    protected override void PaintBody(SKCanvas canvas, int width, int height)
    {
        var y = BodyTop;
        if (_rows.Count == 0)
        {
            Theme.DrawText(canvas, "Nothing to share", Theme.BodyFont, Padding + 8, y + 21, Theme.Muted);
            y += RowHeight;
        }

        for (var i = _scroll; i < Math.Min(_rows.Count, _scroll + MaxRows); i++)
        {
            var row = _rows[i];
            if (row.Selected || _hotRow == i)
            {
                Theme.Fill.Color = row.Selected ? Theme.RowSelected : Theme.RowHot;
                canvas.DrawRoundRect(new SKRect(Padding, y, width - Padding, y + RowHeight), 4, 4, Theme.Fill);
            }

            if (_multiple)
            {
                DrawCheckbox(canvas, Padding + 8, y + 8, row.Selected);
            }

            var textX = Padding + (_multiple ? 34 : 10);
            var kind = row.Source.Kind == PromptSourceKinds.Window ? "window" : "monitor";
            Theme.DrawText(canvas, row.Title, Theme.BodyFont, textX, y + 20, Theme.Foreground, width - textX - Padding - 70);
            Theme.DrawText(canvas, kind, Theme.SmallFont, width - Padding - 60, y + 20, Theme.Muted);
            y += RowHeight;
        }

        if (_offerPersist)
        {
            var persistRow = PersistRowIndex;
            if (_hotRow == persistRow)
            {
                Theme.Fill.Color = Theme.RowHot;
                canvas.DrawRoundRect(new SKRect(Padding, y, width - Padding, y + RowHeight), 4, 4, Theme.Fill);
            }

            DrawCheckbox(canvas, Padding + 8, y + 8, Persist);
            Theme.DrawText(canvas, "Remember this choice", Theme.BodyFont, Padding + 34, y + 21, Theme.Foreground);
        }
    }

    private int PersistRowIndex => _rows.Count;

    private int RowAt(double x, double y)
    {
        if (x < Padding || x >= SurfaceWidth - Padding || y < BodyTop)
        {
            return -1;
        }

        var slot = (int)((y - BodyTop) / RowHeight);
        if (slot < VisibleRows)
        {
            return _scroll + slot;
        }

        return _offerPersist && slot == VisibleRows ? PersistRowIndex : -1;
    }

    /// <summary>
    /// Highlights the row under the pointer.
    /// </summary>
    /// <param name="x">The horizontal position in surface coordinates.</param>
    /// <param name="y">The vertical position in surface coordinates.</param>
    /// <returns><see langword="true"/> when the highlighted row changed.</returns>
    protected override bool BodyPointerMove(double x, double y)
    {
        var row = RowAt(x, y);
        var changed = row != _hotRow;
        _hotRow = row;
        return changed;
    }

    /// <summary>
    /// Selects or toggles the row under the pointer. With a single selection, the row replaces the selection.
    /// </summary>
    /// <param name="x">The horizontal position in surface coordinates.</param>
    /// <param name="y">The vertical position in surface coordinates.</param>
    /// <returns><see langword="true"/> when a row changed.</returns>
    protected override bool BodyPointerRelease(double x, double y)
    {
        var row = RowAt(x, y);
        if (row < 0)
        {
            return false;
        }

        Toggle(row);
        return true;
    }

    /// <summary>
    /// Scrolls the rows by one row in the direction of the vertical value.
    /// </summary>
    /// <param name="dx">The horizontal scroll value. It is ignored.</param>
    /// <param name="dy">The vertical scroll value.</param>
    /// <returns><see langword="true"/> when the rows scrolled.</returns>
    public override bool PointerAxis(double dx, double dy)
    {
        var next = Math.Clamp(_scroll + (dy > 0 ? 1 : dy < 0 ? -1 : 0), 0, Math.Max(0, _rows.Count - MaxRows));
        if (next == _scroll)
        {
            return false;
        }

        _scroll = next;
        return true;
    }

    private void Toggle(int index)
    {
        if (index == PersistRowIndex)
        {
            Persist = !Persist;
            return;
        }

        if (_multiple)
        {
            _rows[index].Selected = !_rows[index].Selected;
            return;
        }

        foreach (var row in _rows)
        {
            row.Selected = false;
        }

        _rows[index].Selected = true;
    }

    /// <summary>
    /// Toggles the highlighted row on Space. The Up and Down keys move the selection and keep it visible.
    /// </summary>
    /// <param name="key">The evdev keycode.</param>
    /// <returns><see langword="true"/> when the key had an effect.</returns>
    protected override bool BodyKey(uint key)
    {
        switch (key)
        {
            case InputCodes.KeySpace when _hotRow >= 0:
                Toggle(_hotRow);
                return true;
            case InputCodes.KeyDown or InputCodes.KeyUp when _rows.Count > 0:
                var current = _rows.FindIndex(r => r.Selected);
                var next = Math.Clamp(current + (key == InputCodes.KeyDown ? 1 : -1), 0, _rows.Count - 1);
                if (!_multiple)
                {
                    foreach (var row in _rows)
                    {
                        row.Selected = false;
                    }
                }

                _rows[next].Selected = true;
                _scroll = Math.Clamp(_scroll, next - MaxRows + 1, next);
                return true;
            default:
                return false;
        }
    }

    private sealed class Row(SelectedSource source, string title, string detail)
    {
        public SelectedSource Source { get; } = source;

        public string Title { get; } = title;

        public string Detail { get; } = detail;

        public bool Selected { get; set; }
    }
}
