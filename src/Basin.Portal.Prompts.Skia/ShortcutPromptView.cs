using Basin.Capabilities;
using Basin.Config;
using SkiaSharp;

namespace Basin.Portal.Prompts.Skia;

public sealed class ShortcutPromptView : SkiaPromptView
{
    private readonly List<Row> _rows = [];
    private readonly IKeymapLookup? _keymap;
    private int _hotRow = -1;
    private int _capturing = -1;

    public ShortcutPromptView(SkiaPromptTheme theme, in ShortcutPrompt prompt, IKeymapLookup? keymap)
        : base(theme, "Bind shortcuts", SkiaPortalPrompts.AppLine(prompt.AppId, prompt.DisplayName), prompt.IconPath)
    {
        _keymap = keymap;
        foreach (var row in prompt.Shortcuts)
        {
            var initial = !string.IsNullOrEmpty(row.CurrentTrigger)
                ? row.CurrentTrigger
                : row.PreferredTaken ? "" : row.PreferredTrigger;
            _rows.Add(new Row(row.Id, row.Description, initial, row.PreferredTaken));
        }

        AddButton("Cancel", PromptResponse.Denied, primary: false);
        AddButton("Bind", PromptResponse.Accepted, primary: true);
    }

    public IReadOnlyList<ShortcutBinding> Bindings =>
        _rows.Where(r => !string.IsNullOrEmpty(r.Trigger)).Select(r => new ShortcutBinding(r.Id, r.Trigger)).ToList();

    public bool IsCapturing => _capturing >= 0;

    public override int Height => BodyTop + (Math.Max(1, _rows.Count) * RowHeight) + 28 + ButtonHeight + Padding + 12;

    protected override void PaintBody(SKCanvas canvas, int width, int height)
    {
        var y = BodyTop;
        for (var i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            if (_hotRow == i || _capturing == i)
            {
                Theme.Fill.Color = _capturing == i ? Theme.RowSelected : Theme.RowHot;
                canvas.DrawRoundRect(new SKRect(Padding, y, width - Padding, y + RowHeight), 4, 4, Theme.Fill);
            }

            var label = string.IsNullOrEmpty(row.Description) ? row.Id : row.Description;
            Theme.DrawText(canvas, label, Theme.BodyFont, Padding + 10, y + 21, Theme.Foreground, width / 2 - Padding);
            var trigger = _capturing == i ? "press a chord…" : string.IsNullOrEmpty(row.Trigger) ? (row.PreferredTaken ? "taken" : "unbound") : Describe(row.Trigger);
            var color = _capturing == i ? Theme.Accent : string.IsNullOrEmpty(row.Trigger) ? Theme.Muted : Theme.Foreground;
            Theme.DrawText(canvas, trigger, Theme.BodyFont, width / 2 + 10, y + 21, color, width / 2 - Padding - 10);
            y += RowHeight;
        }

        Theme.DrawText(canvas, "Click a row and press the chord to bind it; Backspace clears it.", Theme.SmallFont, Padding, y + 18, Theme.Muted, width - 2 * Padding);
    }

    private static string Describe(string trigger) =>
        TriggerSyntax.TryParse(trigger, out var keysym, out var modifiers) ? TriggerSyntax.Describe(keysym, modifiers) : trigger;

    private int RowAt(double x, double y)
    {
        if (x < Padding || x >= SurfaceWidth - Padding || y < BodyTop)
        {
            return -1;
        }

        var row = (int)((y - BodyTop) / RowHeight);
        return row < _rows.Count ? row : -1;
    }

    protected override bool BodyPointerMove(double x, double y)
    {
        var row = RowAt(x, y);
        var changed = row != _hotRow;
        _hotRow = row;
        return changed;
    }

    protected override bool BodyPointerRelease(double x, double y)
    {
        var row = RowAt(x, y);
        if (row < 0)
        {
            var was = _capturing;
            _capturing = -1;
            return was >= 0;
        }

        _capturing = _capturing == row ? -1 : row;
        return true;
    }

    public override bool Key(uint key, bool pressed)
    {
        if (_capturing < 0 || !pressed)
        {
            return base.Key(key, pressed);
        }

        if (key == InputCodes.KeyEsc)
        {
            _capturing = -1;
            return true;
        }

        if (key == InputCodes.KeyBackspace)
        {
            _rows[_capturing].Trigger = "";
            _capturing = -1;
            return true;
        }

        if (IsModifierKey(key))
        {
            return false;
        }

        var keysym = _keymap?.KeysymForKeycode(key) ?? Keysym.NoSymbol;
        if (keysym == Keysym.NoSymbol)
        {
            return false;
        }

        _rows[_capturing].Trigger = TriggerSyntax.Format(keysym, HeldModifiers);
        _capturing = -1;
        return true;
    }

    private static bool IsModifierKey(uint key) => key is InputCodes.KeyLeftShift or InputCodes.KeyRightShift
        or InputCodes.KeyLeftCtrl or InputCodes.KeyRightCtrl or InputCodes.KeyLeftAlt or InputCodes.KeyRightAlt
        or InputCodes.KeyLeftMeta or InputCodes.KeyRightMeta or InputCodes.KeyCapsLock;

    private sealed class Row(string id, string description, string trigger, bool preferredTaken)
    {
        public string Id { get; } = id;

        public string Description { get; } = description;

        public string Trigger { get; set; } = trigger;

        public bool PreferredTaken { get; } = preferredTaken;
    }
}
