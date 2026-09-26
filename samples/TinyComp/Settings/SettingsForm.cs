using System.Globalization;
using Basin.Config;
using Basin.Diagnostics;
using Basin.UI.Paper;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Tomlyn.Model;

namespace TinyComp;

internal sealed class SettingsForm
{
    private static readonly string[] SectionPrefixes =
        ["compositor.", "frame.", "color.", "effects.", "canvas.", "overview.", "hypr.", "bindings.", "rule#", "output.", "settings."];

    private static readonly IReadOnlyList<string> InheritOnOff = ["(inherit)", "on", "off"];

    private readonly SettingsDraft _draft;
    private readonly SettingsContext _context;
    private readonly Dictionary<string, SettingSlot> _slots = new(StringComparer.Ordinal);
    private readonly bool[] _sectionDirty = new bool[SettingsCatalog.Sections.Count];
    private SettingsTheme? _theme;
    private string _themePalette = string.Empty;
    private double _themeSize;
    private int _dirtyVersion = -1;
    private int _output;
    private string? _confirmReset;

    public SettingsForm(SettingsDraft draft, SettingsContext context)
    {
        _draft = draft;
        _context = context;
    }

    public SettingsDraft Draft => _draft;

    public int Section { get; set; }

    public void Build(Prowl.PaperUI.Paper paper)
    {
        var theme = Theme();
        RefreshSectionDirty();
        using (paper.Column("settings").Size(paper.Stretch()).BackgroundColor(theme.Background)
            .BorderColor(theme.Border).BorderWidth(1).Rounded(8).Clip().Enter())
        {
            Header(paper, theme);
            using (paper.Row("body").Width(paper.Stretch()).Height(paper.Stretch()).Enter())
            {
                SettingsWidgets.SectionNav(paper, theme, "nav", SettingsCatalog.Sections, Section, i => Section = i, _sectionDirty);
                using (SettingsWidgets.Scroll(paper, theme, "content"))
                {
                    if (_draft.SaveBlocked is { } blocked)
                    {
                        SettingsWidgets.Note(paper, theme, "blocked", blocked, theme.Warning);
                    }

                    var section = SettingsCatalog.Sections[Math.Clamp(Section, 0, SettingsCatalog.Sections.Count - 1)];
                    SectionHeading(paper, theme, section);
                    switch (section)
                    {
                        case SettingsCatalog.Bindings:
                            BindingsSection(paper, theme);
                            break;
                        case SettingsCatalog.Rules:
                            RulesSection(paper, theme);
                            break;
                        case SettingsCatalog.Outputs:
                            OutputsSection(paper, theme);
                            break;
                        default:
                            CatalogSection(paper, theme, section);
                            if (section == SettingsCatalog.Effects)
                            {
                                ShaderChain(paper, theme);
                            }
                            else if (section == SettingsCatalog.Hypr)
                            {
                                Shortcuts(paper, theme);
                            }
                            else if (section == SettingsCatalog.Canvas)
                            {
                                SettingsWidgets.Note(paper, theme, "canvas-note", "The overview reads sides, shelf, shelf_scale, slope_window, grid, drag and animation_ms from here.");
                            }

                            break;
                    }
                }
            }
        }
    }

    private void SectionHeading(Prowl.PaperUI.Paper paper, SettingsTheme theme, string section)
    {
        using (paper.Row("heading-row").Width(paper.Stretch()).Height(UnitValue.Auto).Gap(8)
            .AlignItems(LayoutAlignment.End).Enter())
        {
            using (paper.Column("heading-text").Width(paper.Stretch()).Height(UnitValue.Auto).Enter())
            {
                SettingsWidgets.Heading(paper, theme, "heading", section);
            }

            if (_confirmReset == section)
            {
                SettingsWidgets.Button(paper, theme, "reset-cancel", "Cancel", () => _confirmReset = null);
                SettingsWidgets.Button(paper, theme, "reset-confirm", "Reset " + section + " to defaults", () =>
                {
                    _confirmReset = null;
                    ResetSection(section);
                }, primary: true);
            }
            else
            {
                SettingsWidgets.Button(paper, theme, "reset", "Reset to defaults", () => _confirmReset = section);
            }
        }

        if (_confirmReset == section)
        {
            SettingsWidgets.Note(paper, theme, "reset-note", ResetNote(section), theme.Warning);
        }
    }

    private static string ResetNote(string section) => section switch
    {
        SettingsCatalog.Bindings => "Every row goes, and the built-in bindings apply again. Nothing is saved until Save.",
        SettingsCatalog.Rules => "Every rule goes. Nothing is saved until Save.",
        SettingsCatalog.Outputs => "Every per-output setting goes, for connected and absent outputs alike. Nothing is saved until Save.",
        SettingsCatalog.Hypr => "The flags go back to their defaults and every global shortcut row goes. Nothing is saved until Save.",
        SettingsCatalog.Effects => "Every effect goes back to its default and the preset chain goes. Nothing is saved until Save.",
        _ => "Every setting here goes back to its default. Nothing is saved until Save.",
    };

    public void ResetSection(string section)
    {
        var keys = SettingsCatalog.Keys.Where(key => key.Section == section).ToList();
        _draft.Reset(keys, document =>
        {
            switch (section)
            {
                case SettingsCatalog.Bindings:
                    RemoveAll(document, "bindings");
                    break;
                case SettingsCatalog.Hypr:
                    RemoveAll(document, "shortcuts");
                    break;
                case SettingsCatalog.Rules:
                    RemoveTables(document, "rule");
                    break;
                case SettingsCatalog.Effects:
                    RemoveTables(document, "effects.shader");
                    break;
                case SettingsCatalog.Outputs:
                    foreach (var header in document.TableNames())
                    {
                        if (header is ["output", var name])
                        {
                            RemoveAll(document, $"output.{TomlValue.Key(name)}");
                        }
                    }

                    break;
            }
        }, SectionPrefixes[SettingsCatalog.Sections.ToList().IndexOf(section)].TrimEnd('.', '#'));
    }

    private static void RemoveAll(TomlDocument document, string table)
    {
        foreach (var key in document.Keys(table))
        {
            document.Remove(table, key);
        }
    }

    private static void RemoveTables(TomlDocument document, string name)
    {
        for (var i = document.TableCount(name) - 1; i >= 0; i--)
        {
            document.RemoveTable(name, i);
        }
    }

    private SettingsTheme Theme()
    {
        var palette = _draft.TextOf("settings", "palette", "dark");
        var size = _draft.Get("settings", "font_size") switch
        {
            long number => number,
            double real => real,
            _ => 14.0,
        };
        size = Math.Clamp(size, 10, 24);
        if (_theme is null || palette != _themePalette || size != _themeSize)
        {
            _themePalette = palette;
            _themeSize = size;
            _theme = palette == "light"
                ? SettingsTheme.Light(_context.Font, (float)size)
                : SettingsTheme.Dark(_context.Font, (float)size);
        }

        return _theme;
    }

    private void RefreshSectionDirty()
    {
        if (_dirtyVersion == _draft.Version)
        {
            return;
        }

        _dirtyVersion = _draft.Version;
        Array.Clear(_sectionDirty);
        foreach (var path in _draft.DirtyPaths)
        {
            for (var i = 0; i < SectionPrefixes.Length; i++)
            {
                if (path.StartsWith(SectionPrefixes[i], StringComparison.Ordinal) ||
                    (i == 6 && path.StartsWith("shortcuts.", StringComparison.Ordinal)))
                {
                    _sectionDirty[i] = true;
                }
            }
        }
    }

    private void Header(Prowl.PaperUI.Paper paper, SettingsTheme theme)
    {
        var dirty = _draft.DirtyCount;
        using (paper.Row("header").Width(paper.Stretch()).Height(MathF.Round(theme.RowHeight * 1.5f))
            .Padding(16, 12, 0, 0).Gap(8).AlignItems(LayoutAlignment.Center).BackgroundColor(theme.Surface).Enter())
        {
            var title = dirty == 0 ? "Settings" : $"Settings  ({dirty} unsaved)";
            paper.Box("title").Width(paper.Stretch()).Height(paper.Stretch())
                .Text(title, theme.Font).FontSize(MathF.Round(theme.FontSize * 1.15f)).TextColor(theme.Text)
                .Alignment(TextAlignment.MiddleLeft).Cursor(PaperCursor.Grab).CursorDragging(PaperCursor.Grabbing)
                .OnPress(_context, static (context, _) => context.DragStart?.Invoke());
            if (_draft.Status.Length > 0)
            {
                paper.Box("status").Width(UnitValue.Auto).Height(paper.Stretch())
                    .Text(_draft.Status, theme.Font).FontSize(theme.Small).TextColor(theme.Dim)
                    .Alignment(TextAlignment.MiddleRight).IsNotInteractable();
            }

            if (_draft.Conflict)
            {
                SettingsWidgets.Button(paper, theme, "overwrite", "Overwrite", () => _context.Overwrite?.Invoke(), primary: true);
                SettingsWidgets.Button(paper, theme, "reload", "Reload", () => _context.Revert?.Invoke());
            }
            else
            {
                SettingsWidgets.Button(paper, theme, "revert", "Revert", () => _context.Revert?.Invoke(), enabled: dirty > 0);
                SettingsWidgets.Button(
                    paper, theme, "save", "Save", () => _context.Save?.Invoke(),
                    enabled: dirty > 0 && _draft.SaveBlocked is null, primary: true);
            }

            SettingsWidgets.Button(paper, theme, "close", "Close", () => _context.Close?.Invoke());
        }
    }

    private void CatalogSection(Prowl.PaperUI.Paper paper, SettingsTheme theme, string section)
    {
        string? group = null;
        foreach (var key in SettingsCatalog.Keys)
        {
            if (key.Section != section || (key.Visible is { } visible && !visible(_draft)))
            {
                continue;
            }

            if (key.Group != group)
            {
                group = key.Group;
                if (group is not null)
                {
                    SettingsWidgets.Heading(paper, theme, "group-" + group, group);
                }
            }

            CatalogRow(paper, theme, key);
        }
    }

    private void CatalogRow(Prowl.PaperUI.Paper paper, SettingsTheme theme, SettingKey key)
    {
        var slot = Slot(key);
        switch (key.Kind)
        {
            case SettingKind.Flag:
                SettingsWidgets.ToggleRow(paper, theme, key.Path, key.Label, slot);
                break;
            case SettingKind.Choice:
                SettingsWidgets.EnumRow(paper, theme, key.Path, key.Label, slot, ChoicesOf(key));
                break;
            case SettingKind.Integer:
                SettingsWidgets.NumberRow(paper, theme, key.Path, key.Label, slot, "0", key.Min, key.Max);
                break;
            case SettingKind.Real when key.Step > 0 && double.IsFinite(key.Min) && double.IsFinite(key.Max):
                SettingsWidgets.SliderRow(paper, theme, key.Path, key.Label, slot, key.Min, key.Max, key.Step);
                break;
            case SettingKind.Real:
                SettingsWidgets.NumberRow(paper, theme, key.Path, key.Label, slot, "0.###", key.Min, key.Max);
                break;
            case SettingKind.Color:
                SettingsWidgets.ColorRow(paper, theme, key.Path, key.Label, slot);
                break;
            case SettingKind.Names:
                NamesRow(paper, theme, key, slot);
                break;
            case SettingKind.Reals:
                RealsRow(paper, theme, key, slot);
                break;
            case SettingKind.Literal:
                SettingsWidgets.TextRow(paper, theme, key.Path, key.Label, slot, key.Default ?? "automatic");
                break;
            case SettingKind.PerSide:
                PerSideRow(paper, theme, key, slot);
                break;
            case SettingKind.OptionalReal:
                SettingsWidgets.ToggleRow(paper, theme, key.Path + "-automatic", key.Label + " automatic", new AutomaticToggle(slot, key.Suggested));
                if (slot.Read() is not null)
                {
                    SettingsWidgets.SliderRow(paper, theme, key.Path, "    " + key.Label, slot, key.Min, key.Max, key.Step);
                }

                break;
            default:
                if (key.Presets is { } presets)
                {
                    PresetButtons(paper, theme, key, slot, presets);
                }

                SettingsWidgets.TextRow(paper, theme, key.Path, key.Label, slot);
                if (key.Kind == SettingKind.Path && ((ISettingValue<string>)slot).Value is { Length: > 0 } path &&
                    path != "none" && key.Presets?.Contains(path) != true && !File.Exists(ExpandFor(key, path)))
                {
                    SettingsWidgets.Note(paper, theme, key.Path + "-missing", $"{path} does not exist", theme.Warning);
                }

                break;
        }

        if (key.Note is { } note)
        {
            SettingsWidgets.Note(paper, theme, key.Path + "-note", note);
        }
    }

    private static void PresetButtons(
        Prowl.PaperUI.Paper paper, SettingsTheme theme, SettingKey key, SettingSlot slot, IReadOnlyList<string> presets)
    {
        var current = ((ISettingValue<string>)slot).Value;
        using (paper.Row(key.Path + "-presets").Width(paper.Stretch()).Height(UnitValue.Auto).PaddingLeft(14).Gap(6).Enter())
        {
            foreach (var preset in presets)
            {
                var name = preset;
                SettingsWidgets.Button(
                    paper, theme, name, name, () => ((ISettingValue<string>)slot).Value = name,
                    primary: string.Equals(current, name, StringComparison.Ordinal));
            }
        }
    }

    private string ExpandFor(SettingKey key, string path) =>
        key.Presets is null ? Expand(path) : OverviewSetting.ResolveTexturePath(path, _context.ConfigPath);

    private IReadOnlyList<string> ChoicesOf(SettingKey key) => key.ChoicesFrom switch
    {
        "renderers" => _context.Renderers,
        "metacity-themes" => _context.MetacityThemes,
        _ => key.Choices ?? [],
    };

    private SettingSlot Slot(SettingKey key)
    {
        if (!_slots.TryGetValue(key.Path, out var slot))
        {
            slot = new SettingSlot(_draft, _context, key.Table, key.Key, key.Kind, key.Default) { Key = key };
            _slots[key.Path] = slot;
        }

        return slot;
    }

    private void NamesRow(Prowl.PaperUI.Paper paper, SettingsTheme theme, SettingKey key, SettingSlot slot)
    {
        var names = new List<string>();
        switch (slot.Read() ?? Parsed(key.Default))
        {
            case string single when single != "none":
                names.Add(single);
                break;
            case TomlArray array:
                foreach (var item in array)
                {
                    if (item is string name && name != "none")
                    {
                        names.Add(name);
                    }
                }

                break;
        }

        var choices = key.Choices ?? [];
        SettingsWidgets.ListRow(
            paper, theme, key.Path, key.Label, names,
            (p, i) => SettingsWidgets.EnumRow(p, theme, "item", "", new ItemValue<string>(names, i, () => WriteNames(slot, names)), choices),
            () => FirstUnused(choices, names), () => WriteNames(slot, names), slot.IsDirty, slot.Badge, slot.Error, slot.Hint);
    }

    private static void WriteNames(SettingSlot slot, List<string> names) =>
        slot.Write(TomlValue.Array(names.Select(TomlValue.From)));

    private void RealsRow(Prowl.PaperUI.Paper paper, SettingsTheme theme, SettingKey key, SettingSlot slot)
    {
        var values = new List<double>();
        if (slot.Read() is TomlArray array)
        {
            foreach (var item in array)
            {
                if (item is long or double)
                {
                    values.Add(Convert.ToDouble(item, CultureInfo.InvariantCulture));
                }
            }
        }

        SettingsWidgets.ListRow(
            paper, theme, key.Path, key.Label, values,
            (p, i) => SettingsWidgets.NumberRow(p, theme, "item", $"Output {i + 1}", new ItemValue<double>(values, i, () => WriteReals(slot, values)), "0.##", key.Min, key.Max),
            () => 1.0, () => WriteReals(slot, values), slot.IsDirty, slot.Badge, slot.Error, slot.Hint);
    }

    private static void WriteReals(SettingSlot slot, List<double> values) =>
        slot.Write(TomlValue.Array(values.Select(TomlValue.From)));

    private void PerSideRow(Prowl.PaperUI.Paper paper, SettingsTheme theme, SettingKey key, SettingSlot slot)
    {
        var fallback = double.Parse(key.Default ?? "0", CultureInfo.InvariantCulture);
        SettingsWidgets.ToggleRow(paper, theme, key.Path + "-per-side", key.Label + " per side", new PerSideToggle(slot, fallback));
        if (slot.Read() is not TomlTable)
        {
            SettingsWidgets.SliderRow(paper, theme, key.Path, key.Label, slot, key.Min, key.Max, key.Step);
            return;
        }

        foreach (var side in SettingsCatalog.Sides)
        {
            SettingsWidgets.SliderRow(
                paper, theme, key.Path + "-" + side, "    " + char.ToUpperInvariant(side[0]) + side[1..],
                new SideValue(slot, side, fallback), key.Min, key.Max, key.Step);
        }
    }

    private static double Number(object? value, double fallback) => value switch
    {
        long number => number,
        double real => real,
        _ => fallback,
    };

    private static TomlValue Sides(SettingSlot slot, string? side, double value, double fallback)
    {
        var table = slot.Read() as TomlTable;
        var entries = new List<KeyValuePair<string, TomlValue>>();
        foreach (var name in SettingsCatalog.Sides)
        {
            var current = name == side ? value : Number(table is not null && table.TryGetValue(name, out var raw) ? raw : null, fallback);
            entries.Add(new(name, TomlValue.From(Math.Round(current, 4))));
        }

        return TomlValue.Inline(entries);
    }

    private void BindingsSection(Prowl.PaperUI.Paper paper, SettingsTheme theme)
    {
        SettingsWidgets.Note(paper, theme, "bindings-note", "A row replaces the built-in binding with the same chord, and none unbinds it. Pick exec to run a command.");
        var chords = _draft.Document.Keys("bindings");
        var parsed = new Dictionary<(uint, Modifiers), int>();
        foreach (var chord in chords)
        {
            if (HotkeyParser.TryParseChord(chord, BasinLogger.None, out var keysym, out var modifiers))
            {
                parsed[(keysym, modifiers)] = parsed.TryGetValue((keysym, modifiers), out var seen) ? seen + 1 : 1;
            }
        }

        SettingsWidgets.Button(paper, theme, "add-binding", "Add binding", () =>
            _context.Capture.Begin(new NewKey(_draft, "bindings", TomlValue.From("none"))));
        if (_context.Capture.Target is NewKey { Table: "bindings" })
        {
            SettingsWidgets.Note(paper, theme, "capturing", "Press the chord for the new binding. Escape cancels.", theme.Accent);
        }

        for (var i = 0; i < chords.Count; i++)
        {
            var chord = chords[i];
            var path = "bindings." + chord;
            string? error = null;
            if (!HotkeyParser.TryParseChord(chord, BasinLogger.None, out var keysym, out var modifiers))
            {
                error = "That isn't a valid key combination.";
            }
            else if (parsed[(keysym, modifiers)] > 1)
            {
                error = "Another binding uses the same keys.";
            }

            paper.PushID(i);
            var action = new BindingAction(_draft, chord);
            using (var row = SettingsWidgets.Row(paper, theme, "binding", chord, _draft.IsDirty(path), null, error ?? _draft.ErrorOf(path)))
            {
                SettingsWidgets.ChordButton(paper, theme, "chord", MathF.Round(theme.FontSize * 9f), new BindingChord(_draft, chord), _context.Capture);
                SettingsWidgets.Button(paper, theme, "remove", "Remove", () => _draft.Set("bindings", chord, null));
            }

            SettingsWidgets.EnumRow(paper, theme, "action", "    Action", action, BindingAction.Choices);
            if (action.Value == "exec")
            {
                SettingsWidgets.TextRow(paper, theme, "exec", "    Command", new BindingCommand(_draft, chord), "a command line");
            }

            paper.PopID();
        }
    }

    private void RulesSection(Prowl.PaperUI.Paper paper, SettingsTheme theme)
    {
        SettingsWidgets.Note(paper, theme, "rules-note", "Rules apply to windows mapped after the change. The most specific rule wins, then the order here.");
        SettingsWidgets.Button(paper, theme, "add-rule", "Add rule", () => _draft.Edit(
            document =>
            {
                var index = document.AppendTable("rule");
                document.SetInTable("rule", index, "app_id", TomlValue.From("app.id"));
            },
            "rule"));
        var count = _draft.Document.TableCount("rule");
        for (var i = 0; i < count; i++)
        {
            paper.PushID(i);
            var index = i;
            SettingsWidgets.Heading(paper, theme, "rule-heading", $"Rule {i + 1}");
            using (paper.Row("rule-tools").Width(paper.Stretch()).Height(UnitValue.Auto).Gap(6).Enter())
            {
                SettingsWidgets.Button(paper, theme, "up", "Up", () => _draft.Edit(d => d.MoveTable("rule", index, index - 1), "rule"), enabled: index > 0);
                SettingsWidgets.Button(paper, theme, "down", "Down", () => _draft.Edit(d => d.MoveTable("rule", index, index + 1), "rule"), enabled: index < count - 1);
                SettingsWidgets.Button(paper, theme, "remove", "Remove", () => _draft.Edit(d => d.RemoveTable("rule", index), "rule"));
            }

            SettingsWidgets.TextRow(paper, theme, "app_id", "App id", RuleSlot(i, "app_id", SettingKind.Text), "exact match");
            SettingsWidgets.TextRow(paper, theme, "title_regex", "Title regex", RuleSlot(i, "title_regex", SettingKind.Text), "optional");
            SettingsWidgets.EnumRow(paper, theme, "frame", "Frame", RuleSlot(i, "frame", SettingKind.Choice, "(inherit)"), ["(inherit)", .. SettingsCatalog.FrameStyles]);
            SettingsWidgets.EnumRow(paper, theme, "effects", "Effects", RuleSlot(i, "effects", SettingKind.Choice, "(inherit)", onOff: true), InheritOnOff);
            SettingsWidgets.TextRow(paper, theme, "workspace", "Workspace", RuleSlot(i, "workspace", SettingKind.Literal), "any");
            SettingsWidgets.TextRow(paper, theme, "x", "X", RuleSlot(i, "x", SettingKind.Literal), "placed");
            SettingsWidgets.TextRow(paper, theme, "y", "Y", RuleSlot(i, "y", SettingKind.Literal), "placed");
            SettingsWidgets.TextRow(paper, theme, "width", "Width", RuleSlot(i, "width", SettingKind.Literal), "client");
            SettingsWidgets.TextRow(paper, theme, "height", "Height", RuleSlot(i, "height", SettingKind.Literal), "client");
            SettingsWidgets.EnumRow(
                paper, theme, "canvas", "Canvas", RuleSlot(i, "canvas", SettingKind.Choice, "(none)"),
                ["(none)", "left", "right", "top", "bottom", "top-left", "top-right", "bottom-left", "bottom-right"]);
            paper.PopID();
        }
    }

    private SettingSlot RuleSlot(int index, string key, SettingKind kind, string? inherit = null, bool onOff = false) =>
        new(_draft, _context, "rule", key, kind, null, index) { Inherit = inherit, OnOff = onOff };

    private void OutputsSection(Prowl.PaperUI.Paper paper, SettingsTheme theme)
    {
        var names = new List<string>();
        foreach (var output in _context.Outputs)
        {
            names.Add(output.Name);
        }

        foreach (var header in _draft.Document.TableNames())
        {
            if (header is ["output", var name] && !names.Contains(name))
            {
                names.Add(name);
            }
        }

        if (names.Count == 0)
        {
            SettingsWidgets.Note(paper, theme, "no-outputs", "No outputs are connected or named in the file.");
            return;
        }

        _output = Math.Clamp(_output, 0, names.Count - 1);
        using (paper.Row("tabs").Width(paper.Stretch()).Height(UnitValue.Auto).Gap(6).PaddingBottom(6).Enter())
        {
            for (var i = 0; i < names.Count; i++)
            {
                var index = i;
                paper.PushID(i);
                SettingsWidgets.Button(paper, theme, "tab", names[i], () => _output = index, primary: i == _output);
                paper.PopID();
            }
        }

        var selected = names[_output];
        var connected = _context.Outputs.FirstOrDefault(o => o.Name == selected);
        var table = $"output.{TomlValue.Key(selected)}";
        paper.PushID(selected);
        if (connected is null)
        {
            SettingsWidgets.Note(paper, theme, "absent", "Not connected.", theme.Warning);
        }

        SettingsWidgets.TextRow(paper, theme, "scale", "Scale", OutputSlot(table, "scale", SettingKind.Literal), "automatic");
        SettingsWidgets.EnumRow(paper, theme, "transform", "Transform", OutputSlot(table, "transform", SettingKind.Choice, "(automatic)"), ["(automatic)", .. SettingsCatalog.Transforms]);
        var modes = connected?.Modes ?? [];
        SettingsWidgets.EnumRow(paper, theme, "mode", "Mode", OutputSlot(table, "mode", SettingKind.Choice, "(automatic)"), ["(automatic)", .. modes]);
        SettingsWidgets.EnumRow(paper, theme, "overview", "Overview", OutputSlot(table, "overview", SettingKind.Choice, "(inherit)", onOff: true), InheritOnOff);
        SettingsWidgets.TextRow(paper, theme, "overview_scale", "Overview scale", OutputSlot(table, "overview_scale", SettingKind.Literal), "inherit");
        SettingsWidgets.EnumRow(paper, theme, "overview_wall", "Overview wall", OutputSlot(table, "overview_wall", SettingKind.Choice, "(inherit)"), ["(inherit)", "slope", "step"]);
        SettingsWidgets.Heading(paper, theme, "canvas-overrides", "Canvas overrides");
        SettingsWidgets.Note(paper, theme, "canvas-overrides-note", "Each value is a TOML literal, and an empty field inherits [canvas].");
        foreach (var key in SettingsCatalog.Keys)
        {
            if (key.Section != SettingsCatalog.Canvas)
            {
                continue;
            }

            var inherited = _draft.Document.RawValue("canvas", key.Key) ?? key.Default ?? "automatic";
            SettingsWidgets.TextRow(paper, theme, key.Key, key.Label, OutputSlot(table, key.Key, SettingKind.Literal), inherited);
        }

        paper.PopID();
    }

    private SettingSlot OutputSlot(string table, string key, SettingKind kind, string? inherit = null, bool onOff = false)
    {
        var path = table + "." + key;
        if (!_slots.TryGetValue(path, out var slot))
        {
            slot = new SettingSlot(_draft, _context, table, key, kind, null) { Inherit = inherit, OnOff = onOff };
            _slots[path] = slot;
        }

        return slot;
    }

    private void Shortcuts(Prowl.PaperUI.Paper paper, SettingsTheme theme)
    {
        SettingsWidgets.Heading(paper, theme, "shortcuts", "Global shortcuts");
        SettingsWidgets.Note(paper, theme, "shortcuts-note", "Rows are app_id:id and the chord a client's shortcut fires on.");
        var names = _draft.Document.Keys("shortcuts");
        for (var i = 0; i < names.Count; i++)
        {
            var name = names[i];
            paper.PushID(i);
            using (var row = SettingsWidgets.Row(paper, theme, "shortcut", name, _draft.IsDirty("shortcuts." + name)))
            {
                SettingsWidgets.ChordButton(
                    paper, theme, "chord", MathF.Round(theme.FontSize * 9f),
                    new SettingSlot(_draft, _context, "shortcuts", name, SettingKind.Text, null), _context.Capture);
                SettingsWidgets.Button(paper, theme, "remove", "Remove", () => _draft.Set("shortcuts", name, null));
            }

            paper.PopID();
        }

        SettingsWidgets.TextRow(paper, theme, "new-shortcut", "Add app_id:id", new NewShortcut(_draft), "org.example.app:toggle");
    }

    private void ShaderChain(Prowl.PaperUI.Paper paper, SettingsTheme theme)
    {
        SettingsWidgets.Heading(paper, theme, "chain", "Preset chain");
        SettingsWidgets.Note(paper, theme, "chain-note", "Several presets chain in order, each with its own parameters. The chain replaces the shader key above.");
        SettingsWidgets.Button(paper, theme, "add-preset", "Add preset", () => _draft.Edit(
            document =>
            {
                document.Remove("effects", "shader");
                document.Remove("effects", "shader_params");
                var index = document.AppendTable("effects.shader");
                document.SetInTable("effects.shader", index, "path", TomlValue.From("~/shaders/preset.slangp"));
            },
            "effects.shader"));
        var count = _draft.Document.TableCount("effects.shader");
        var rows = _draft.Array("effects.shader");
        for (var i = 0; i < count; i++)
        {
            paper.PushID(i);
            var index = i;
            SettingsWidgets.Heading(paper, theme, "preset", $"Preset {i + 1}");
            using (paper.Row("preset-tools").Width(paper.Stretch()).Height(UnitValue.Auto).Gap(6).Enter())
            {
                SettingsWidgets.Button(paper, theme, "up", "Up", () => _draft.Edit(d => d.MoveTable("effects.shader", index, index - 1), "effects.shader"), enabled: index > 0);
                SettingsWidgets.Button(paper, theme, "down", "Down", () => _draft.Edit(d => d.MoveTable("effects.shader", index, index + 1), "effects.shader"), enabled: index < count - 1);
                SettingsWidgets.Button(paper, theme, "remove", "Remove", () => _draft.Edit(d => d.RemoveTable("effects.shader", index), "effects.shader"));
            }

            var pathSlot = new SettingSlot(_draft, _context, "effects.shader", "path", SettingKind.Path, null, i);
            SettingsWidgets.TextRow(paper, theme, "path", "Path", pathSlot, "a .slangp path");
            var raw = _draft.Document.RawValueInTable("effects.shader", i, "params");
            if (raw is null && rows is not null && i < rows.Count && rows[i].ContainsKey("params"))
            {
                SettingsWidgets.Note(paper, theme, "params-table", "This preset's params are a [effects.shader.params] table; edit them in the file.");
            }
            else
            {
                SettingsWidgets.TextRow(
                    paper, theme, "params", "Params", new SettingSlot(_draft, _context, "effects.shader", "params", SettingKind.Literal, null, i),
                    "{ CRTgamma = 2.4 }");
                if (((ISettingValue<string>)pathSlot).Value is { Length: > 0 } preset && _context.ShaderParameters?.Invoke(Expand(preset)) is { Count: > 0 } known)
                {
                    SettingsWidgets.Note(paper, theme, "params-known", "Parameters: " + string.Join(", ", known));
                }
            }

            paper.PopID();
        }
    }

    private static string FirstUnused(IReadOnlyList<string> choices, List<string> used)
    {
        foreach (var choice in choices)
        {
            if (!used.Contains(choice))
            {
                return choice;
            }
        }

        return choices.Count > 0 ? choices[0] : string.Empty;
    }

    private static object? Parsed(string? literal) =>
        literal is null ? null : Tomlyn.Toml.ToModel("v = " + literal)["v"];

    private static string Expand(string path) =>
        path.StartsWith("~/", StringComparison.Ordinal)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..])
            : path;

    private sealed class PerSideToggle(SettingSlot slot, double fallback) : ISettingValue<bool>
    {
        public bool Value
        {
            get => slot.Read() is TomlTable;
            set
            {
                if (value == Value)
                {
                    return;
                }

                if (value)
                {
                    slot.Write(Sides(slot, null, 0, Number(slot.Read(), fallback)));
                    return;
                }

                var first = slot.Read() is TomlTable table && table.TryGetValue("left", out var left) ? Number(left, fallback) : fallback;
                slot.Write(TomlValue.From(Math.Round(first, 4)));
            }
        }

        public bool IsDirty => slot.IsDirty;

        public string? Badge => null;

        public string? Error => slot.Error;
    }

    private sealed class AutomaticToggle(SettingSlot slot, double suggested) : ISettingValue<bool>
    {
        public bool Value
        {
            get => slot.Read() is null;
            set
            {
                if (value == Value)
                {
                    return;
                }

                slot.Write(value ? null : TomlValue.From(suggested));
            }
        }

        public bool IsDirty => slot.IsDirty;

        public string? Badge => null;

        public string? Error => Value ? slot.Error : null;

        public string? Hint => Value ? slot.Hint : null;
    }

    private sealed class SideValue(SettingSlot slot, string side, double fallback) : ISettingValue<double>
    {
        public double Value
        {
            get => slot.Read() is TomlTable table && table.TryGetValue(side, out var raw) ? Number(raw, fallback) : fallback;
            set => slot.Write(Sides(slot, side, value, fallback));
        }

        public bool IsDirty => slot.IsDirty;

        public string? Badge => null;

        public string? Error => null;
    }

    private sealed class ItemValue<T>(List<T> items, int index, Action changed) : ISettingValue<T>
    {
        public T Value
        {
            get => items[index];
            set
            {
                items[index] = value;
                changed();
            }
        }

        public bool IsDirty => false;

        public string? Badge => null;

        public string? Error => null;
    }

    private sealed class NewKey(SettingsDraft draft, string table, TomlValue initial) : ISettingValue<string>
    {
        public string Table => table;

        public string Value
        {
            get => string.Empty;
            set
            {
                if (draft.Document.Contains(table, value))
                {
                    draft.SetError(table + "." + value, "Those keys are already bound.");
                    return;
                }

                draft.Set(table, value, initial);
            }
        }

        public bool IsDirty => false;

        public string? Badge => null;

        public string? Error => null;
    }

    private sealed class NewShortcut(SettingsDraft draft) : ISettingValue<string>
    {
        public string Value
        {
            get => string.Empty;
            set
            {
                var name = value.Trim();
                var colon = name.IndexOf(':', StringComparison.Ordinal);
                if (colon <= 0 || colon == name.Length - 1 || draft.Document.Contains("shortcuts", name))
                {
                    return;
                }

                draft.Set("shortcuts", name, TomlValue.From("Super+F12"));
            }
        }

        public bool IsDirty => false;

        public string? Badge => null;

        public string? Error => null;
    }

    private sealed class BindingChord(SettingsDraft draft, string chord) : ISettingValue<string>
    {
        public string Value
        {
            get => chord;
            set => draft.Rename("bindings", chord, value);
        }

        public bool IsDirty => false;

        public string? Badge => null;

        public string? Error => null;
    }

    private sealed class BindingAction(SettingsDraft draft, string chord) : ISettingValue<string>
    {
        public static IReadOnlyList<string> Choices { get; } = [.. Config.ActionNames, "none", "exec"];

        public string Value
        {
            get => draft.Get("bindings", chord) switch
            {
                false => "none",
                string name when Config.ActionFromName(name) is not null || name == "none" => name,
                _ => "exec",
            };

            set
            {
                if (value == "exec")
                {
                    if (Value != "exec")
                    {
                        draft.Set("bindings", chord, TomlValue.Inline([new("exec", TomlValue.From("foot"))]));
                    }

                    return;
                }

                draft.Set("bindings", chord, TomlValue.From(value));
            }
        }

        public bool IsDirty => draft.IsDirty("bindings." + chord);

        public string? Badge => null;

        public string? Error => null;
    }

    private sealed class BindingCommand(SettingsDraft draft, string chord) : ISettingValue<string>
    {
        public string Value
        {
            get => draft.Get("bindings", chord) switch
            {
                TomlTable table when table.TryGetValue("exec", out var exec) => exec switch
                {
                    string text => text,
                    TomlArray words => string.Join(' ', words.OfType<string>()),
                    _ => string.Empty,
                },
                TomlArray words => string.Join(' ', words.OfType<string>()),
                string text when Config.ActionFromName(text) is null && text != "none" => text,
                _ => string.Empty,
            };

            set => draft.Set("bindings", chord, TomlValue.Inline([new("exec", TomlValue.From(value))]));
        }

        public bool IsDirty => false;

        public string? Badge => null;

        public string? Error => null;
    }
}
