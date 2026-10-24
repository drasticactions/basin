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
        ["compositor.", "frame.", "color.", "effects.", "canvas.", "overview.", "physics.", "hypr.", "input.", "touch.", "bindings.", "rule#", "output.", "settings."];

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
    private int _inputTarget;
    private string? _confirmReset;

    public SettingsForm(SettingsDraft draft, SettingsContext context)
    {
        _draft = draft;
        _context = context;
    }

    public SettingsDraft Draft => _draft;

    public int Section { get; set; }

    public int InputTarget
    {
        get => _inputTarget;
        set => _inputTarget = value;
    }

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
                        case SettingsCatalog.Launch:
                            LaunchSection(paper, theme);
                            break;
                        case SettingsCatalog.Outputs:
                            OutputsSection(paper, theme);
                            break;
                        case SettingsCatalog.Input:
                            InputSection(paper, theme);
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
        SettingsCatalog.Bindings => "This removes every binding, and the built-in bindings apply again. Nothing is saved until you click Save.",
        SettingsCatalog.Rules => "This removes every rule. Nothing is saved until you click Save.",
        SettingsCatalog.Outputs => "This removes every output setting, for connected and disconnected outputs. Nothing is saved until you click Save.",
        SettingsCatalog.Input => "This removes every [input] table and [[input.device]] entry, and each device goes back to its default. Values set while running stay. Nothing is saved until you click Save.",
        SettingsCatalog.Hypr => "The switches go back to their defaults, and every global shortcut is removed. Nothing is saved until you click Save.",
        SettingsCatalog.Effects => "Every effect goes back to its default, and the preset chain is removed. Nothing is saved until you click Save.",
        _ => "Every setting here goes back to its default. Nothing is saved until you click Save.",
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
                case SettingsCatalog.Input:
                    RemoveTables(document, Basin.Config.InputConfig.DeviceTable);
                    foreach (var header in document.TableNames())
                    {
                        if (header is ["input", ..])
                        {
                            RemoveAll(document, string.Join('.', header));
                        }
                    }

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
                    (SettingsCatalog.Sections[i] == SettingsCatalog.Hypr && path.StartsWith("shortcuts.", StringComparison.Ordinal)))
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
                SettingsWidgets.Button(paper, theme, "reload", "Revert", () => _context.Revert?.Invoke());
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
                SettingsWidgets.ColorRow(paper, theme, key.Path, key.Label, slot, key.Alpha);
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
                    SettingsWidgets.Note(paper, theme, key.Path + "-missing", $"Couldn't find {path}.", theme.Warning);
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
        "msstyles-themes" => _context.MsStylesThemeNames,
        "msstyles-colors" => MsStylesEntry()?.Colors ?? [],
        "msstyles-sizes" => MsStylesEntry()?.Sizes ?? [],
        _ => key.Choices ?? [],
    };

    private Basin.Themes.MsStyles.MsStylesThemeEntry? MsStylesEntry()
    {
        var name = _draft.TextOf("frame.msstyles", "theme", string.Empty);
        foreach (var entry in _context.MsStylesThemes)
        {
            if (string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return null;
    }

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
        SettingsWidgets.Note(paper, theme, "bindings-note", "A binding replaces the built-in one for the same keys. Pick none to unbind the keys, or exec to run a command.");
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
            SettingsWidgets.Note(paper, theme, "capturing", "Press the keys for the new binding. Press Escape to cancel.", theme.Accent);
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
        SettingsWidgets.Note(paper, theme, "rules-note", "Rules apply to windows that open after the change. The most specific rule wins, then the order here.");
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
            SettingsWidgets.EnumRow(paper, theme, "physics", "Physics body", RuleSlot(i, "physics", SettingKind.Choice, "(inherit)", onOff: true), InheritOnOff);
            SettingsWidgets.EnumRow(paper, theme, "pin", "Pinned", RuleSlot(i, "pin", SettingKind.Choice, "(inherit)", onOff: true), InheritOnOff);
            SettingsWidgets.EnumRow(paper, theme, "no_collide", "No collide", RuleSlot(i, "no_collide", SettingKind.Choice, "(inherit)", onOff: true), InheritOnOff);
            SettingsWidgets.TextRow(paper, theme, "mass", "Mass", RuleSlot(i, "mass", SettingKind.Literal), "density");
            SettingsWidgets.TextRow(paper, theme, "gravity", "Gravity", RuleSlot(i, "gravity", SettingKind.Literal), "scale");
            SettingsWidgets.TextRow(paper, theme, "bounce", "Bounce", RuleSlot(i, "bounce", SettingKind.Literal), "restitution");
            SettingsWidgets.TextRow(paper, theme, "friction", "Friction", RuleSlot(i, "friction", SettingKind.Literal), "surface");
            paper.PopID();
        }
    }

    private void LaunchSection(Prowl.PaperUI.Paper paper, SettingsTheme theme)
    {
        SettingsWidgets.Note(paper, theme, "launch-note", "A launch rule picks the X server for an X11 app before it starts. The low-density server runs at scale 1 and 96 DPI and is stretched to fit the output. A rule applies the next time the app starts. To restart a running app, use the density item in its window menu.");
        SettingsWidgets.Button(paper, theme, "add-launch", "Add launch rule", () => _draft.Edit(
            document =>
            {
                var index = document.AppendTable(Basin.Config.LaunchRules.Table);
                document.SetInTable(Basin.Config.LaunchRules.Table, index, "exec", TomlValue.From("program"));
                document.SetInTable(Basin.Config.LaunchRules.Table, index, "x11", TomlValue.From("low-density"));
            },
            Basin.Config.LaunchRules.Table));
        var count = _draft.Document.TableCount(Basin.Config.LaunchRules.Table);
        for (var i = 0; i < count; i++)
        {
            paper.PushID(i);
            var index = i;
            SettingsWidgets.Heading(paper, theme, "launch-heading", $"Launch rule {i + 1}");
            using (paper.Row("launch-tools").Width(paper.Stretch()).Height(UnitValue.Auto).Gap(6).Enter())
            {
                SettingsWidgets.Button(paper, theme, "remove", "Remove", () => _draft.Edit(d => d.RemoveTable(Basin.Config.LaunchRules.Table, index), Basin.Config.LaunchRules.Table));
            }

            SettingsWidgets.TextRow(paper, theme, "desktop_id", "Desktop id", LaunchSlot(i, "desktop_id", SettingKind.Text), "leafpad.desktop");
            SettingsWidgets.TextRow(paper, theme, "exec", "Program", LaunchSlot(i, "exec", SettingKind.Text), "base name");
            SettingsWidgets.EnumRow(paper, theme, "x11", "X server", LaunchSlot(i, "x11", SettingKind.Choice), ["native", "low-density"]);
            paper.PopID();
        }
    }

    private SettingSlot LaunchSlot(int index, string key, SettingKind kind, string? inherit = null) =>
        new(_draft, _context, Basin.Config.LaunchRules.Table, key, kind, null, index) { Inherit = inherit };

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
        var overviewTable = table + ".overview";
        var overviewFlag = _draft.Document.RawValue(table, "overview");
        var overviewSub = _draft.Document.Keys(overviewTable).Count > 0;
        var overviewSlot = overviewSub
            ? OutputSlot(overviewTable, "enable", SettingKind.Choice, "(inherit)", onOff: true)
            : OutputSlot(table, "overview", SettingKind.Choice, "(inherit)", onOff: true);
        SettingsWidgets.EnumRow(paper, theme, "overview", "Overview", overviewSlot, InheritOnOff);
        SettingsWidgets.TextRow(paper, theme, "overview_scale", "Overview scale", OutputSlot(table, "overview_scale", SettingKind.Literal), "inherit");
        SettingsWidgets.EnumRow(paper, theme, "overview_wall", "Overview wall", OutputSlot(table, "overview_wall", SettingKind.Choice, "(inherit)"), ["(inherit)", "slope", "step"]);
        SettingsWidgets.Heading(paper, theme, "canvas-overrides", "Canvas overrides");
        SettingsWidgets.Note(paper, theme, "canvas-overrides-note", "Type each value as you would in the file. Leave a field empty to use the [canvas] value.");
        foreach (var key in SettingsCatalog.Keys)
        {
            if (key.Section != SettingsCatalog.Canvas)
            {
                continue;
            }

            var inherited = _draft.Document.RawValue("canvas", key.Key) ?? key.Default ?? "automatic";
            SettingsWidgets.TextRow(paper, theme, key.Key, key.Label, OutputSlot(table, key.Key, SettingKind.Literal), inherited);
        }

        SettingsWidgets.Heading(paper, theme, "overview-overrides", "Overview overrides");
        if (overviewFlag is not null)
        {
            SettingsWidgets.Note(
                paper, theme, "overview-overrides-flag",
                $"This output sets overview = {overviewFlag}. Move it to enable in an [{overviewTable}] table to override the overview here.",
                theme.Warning);
        }
        else
        {
            SettingsWidgets.Note(paper, theme, "overview-overrides-note", "Type each value as you would in the file. Leave a field empty to use the [overview] value.");
            foreach (var key in SettingsCatalog.Keys)
            {
                if (key.Section != SettingsCatalog.Overview || (key.Key != "anchor" && Array.IndexOf(OverviewSetting.TerraceKeys, key.Key) < 0))
                {
                    continue;
                }

                var inherited = _draft.Document.RawValue("overview", key.Key) ?? key.Default ?? "automatic";
                SettingsWidgets.TextRow(
                    paper, theme, "overview-" + key.Key, key.Label, OutputSlot(overviewTable, key.Key, SettingKind.Literal), inherited);
            }
        }

        paper.PopID();
    }

    private static readonly Basin.Capabilities.InputSetting[] TouchpadKeys =
    [
        Basin.Capabilities.InputSetting.Tap, Basin.Capabilities.InputSetting.TapButtonMap, Basin.Capabilities.InputSetting.Drag,
        Basin.Capabilities.InputSetting.DragLock, Basin.Capabilities.InputSetting.ThreeFingerDrag,
        Basin.Capabilities.InputSetting.NaturalScroll, Basin.Capabilities.InputSetting.ClickMethod,
        Basin.Capabilities.InputSetting.ClickfingerButtonMap, Basin.Capabilities.InputSetting.ScrollMethod,
        Basin.Capabilities.InputSetting.DisableWhileTyping, Basin.Capabilities.InputSetting.AccelProfile,
        Basin.Capabilities.InputSetting.AccelSpeed, Basin.Capabilities.InputSetting.LeftHanded,
        Basin.Capabilities.InputSetting.MiddleEmulation, Basin.Capabilities.InputSetting.ScrollFactor,
        Basin.Capabilities.InputSetting.SendEvents,
    ];

    private static readonly Basin.Capabilities.InputSetting[] PointerKeys =
    [
        Basin.Capabilities.InputSetting.AccelProfile, Basin.Capabilities.InputSetting.AccelSpeed,
        Basin.Capabilities.InputSetting.NaturalScroll, Basin.Capabilities.InputSetting.LeftHanded,
        Basin.Capabilities.InputSetting.MiddleEmulation, Basin.Capabilities.InputSetting.ScrollMethod,
        Basin.Capabilities.InputSetting.ScrollButton, Basin.Capabilities.InputSetting.ScrollButtonLock,
        Basin.Capabilities.InputSetting.DisableWhileTrackpointing, Basin.Capabilities.InputSetting.ScrollFactor,
        Basin.Capabilities.InputSetting.Rotation, Basin.Capabilities.InputSetting.MapToOutput,
    ];

    private static readonly Basin.Capabilities.InputSetting[] KeyboardKeys =
    [
        Basin.Capabilities.InputSetting.KeymapLayout, Basin.Capabilities.InputSetting.KeymapVariant,
        Basin.Capabilities.InputSetting.KeymapModel, Basin.Capabilities.InputSetting.KeymapOptions,
        Basin.Capabilities.InputSetting.KeymapRules, Basin.Capabilities.InputSetting.RepeatRate,
        Basin.Capabilities.InputSetting.RepeatDelay,
    ];

    private void InputSection(Prowl.PaperUI.Paper paper, SettingsTheme theme)
    {
        var devices = new List<Basin.Capabilities.InputDeviceInfo>();
        if (_context.Input is { } input)
        {
            var span = new Basin.Capabilities.InputDeviceInfo[32];
            var count = input.Enumerate(span);
            while (count < 0)
            {
                span = new Basin.Capabilities.InputDeviceInfo[span.Length * 2];
                count = input.Enumerate(span);
            }

            for (var i = 0; i < count; i++)
            {
                if (devices.FindIndex(d => d.Name == span[i].Name) < 0)
                {
                    devices.Add(span[i]);
                }
            }
        }

        string[] types = ["All touchpads", "All pointers", "All keyboards"];
        var labels = new List<string>(types);
        foreach (var device in devices)
        {
            labels.Add(device.Name);
        }

        _inputTarget = Math.Clamp(_inputTarget, 0, labels.Count - 1);
        using (paper.Row("targets").Width(paper.Stretch()).Height(UnitValue.Auto).Gap(6).PaddingBottom(6).Enter())
        {
            for (var i = 0; i < labels.Count; i++)
            {
                var index = i;
                paper.PushID(i);
                SettingsWidgets.Button(paper, theme, "target", labels[i], () => _inputTarget = index, primary: i == _inputTarget);
                paper.PopID();
            }
        }

        Basin.Capabilities.InputMatch match;
        Basin.Capabilities.InputDeviceInfo? chosen = null;
        IEnumerable<Basin.Capabilities.InputSetting> keys;
        switch (_inputTarget)
        {
            case 0:
                match = new Basin.Capabilities.InputMatch(Type: Basin.Capabilities.InputDeviceType.Touchpad);
                keys = TouchpadKeys;
                break;
            case 1:
                match = new Basin.Capabilities.InputMatch(Type: Basin.Capabilities.InputDeviceType.Pointer);
                keys = PointerKeys;
                break;
            case 2:
                match = new Basin.Capabilities.InputMatch(AnyKeyboard: true);
                keys = KeyboardKeys;
                break;
            default:
                chosen = devices[_inputTarget - types.Length];
                match = new Basin.Capabilities.InputMatch(NameGlob: chosen.Value.Name);
                keys = Basin.Capabilities.InputSettingNames.Settings.ToArray();
                break;
        }

        if (_context.Input is null)
        {
            SettingsWidgets.Note(paper, theme, "no-input", "This session can't apply input settings live. They still save to the file.", theme.Warning);
        }
        else if (chosen is null && devices.Count == 0)
        {
            SettingsWidgets.Note(paper, theme, "no-devices", "No input devices are connected. Values still save to the file.");
        }

        var target = labels[_inputTarget];
        paper.PushID(target);
        foreach (var setting in keys)
        {
            var support = chosen is { } one && _context.Input is { } live
                ? live.Support(one.Id, setting)
                : new Basin.Capabilities.InputSettingSupport(true, uint.MaxValue, uint.MaxValue);
            if (!support.Supported)
            {
                continue;
            }

            string? badge = null;
            var current = string.Empty;
            if (chosen is { } device && _context.Input is { } configuration)
            {
                if (configuration is Basin.Capabilities.IInputSettingSources sources)
                {
                    badge = Basin.Capabilities.InputSettingNames.LayerName(sources.SourceOf(device.Id, setting));
                }

                if (configuration.TryGet(device.Id, setting, out var now))
                {
                    current = Basin.Capabilities.InputSettingNames.Format(setting, now);
                }
            }

            var slot = new InputSlot(_draft, match, setting, badge, current);
            var key = Basin.Capabilities.InputSettingNames.Key(setting);
            var label = key.Replace('_', ' ');
            if (slot.IsChoice)
            {
                var choices = new List<string> { InputSlot.Default };
                foreach (var word in Basin.Capabilities.InputSettingNames.Words(setting))
                {
                    if (Basin.Capabilities.InputSettingNames.IsSupportedWord(setting, word, support))
                    {
                        choices.Add(word);
                    }
                }

                SettingsWidgets.EnumRow(paper, theme, key, label, slot, choices);
            }
            else
            {
                SettingsWidgets.TextRow(paper, theme, key, label, slot, current.Length > 0 ? current : "default");
            }
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
        SettingsWidgets.Note(paper, theme, "shortcuts-note", "Each row is an app_id:id and the keys that fire that app's shortcut.");
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
        SettingsWidgets.Note(paper, theme, "chain-note", "Presets run in order, each with its own parameters. The chain replaces the shader preset above.");
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
                SettingsWidgets.Note(paper, theme, "params-table", "This preset's parameters are in an [effects.shader.params] table. Edit them in the file.");
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
