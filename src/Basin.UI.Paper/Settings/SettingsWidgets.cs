using System.Globalization;
using Prowl.PaperUI;
using Prowl.PaperUI.Events;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using TextWrapMode = Prowl.Scribe.TextWrapMode;
using Prowl.Vector;

namespace Basin.UI.Paper;

public static class SettingsWidgets
{
    private const float ScrollStep = 40f;
    private const float ScrollBarWidth = 6f;

    public static RowScope Row(
        Prowl.PaperUI.Paper paper, SettingsTheme theme, string id, string label, bool dirty = false,
        string? badge = null, string? error = null, string? hint = null)
    {
        ArgumentNullException.ThrowIfNull(paper);
        ArgumentNullException.ThrowIfNull(theme);

        paper.PushID(id);
        var outer = paper.Column("row").Width(paper.Stretch()).Height(UnitValue.Auto).Enter();
        var line = paper.Row("line").Width(paper.Stretch()).Height(theme.RowHeight).Gap(8)
            .AlignItems(LayoutAlignment.Center).Enter();
        paper.Box("dirty").Size(6, 6).Rounded(3)
            .BackgroundColor(dirty ? theme.Accent : new Color32(0, 0, 0, 0)).IsNotInteractable();
        paper.Box("label").Width(paper.Stretch()).Height(paper.Stretch())
            .Text(label, theme.Font).FontSize(theme.FontSize).TextColor(theme.Text)
            .Alignment(TextAlignment.MiddleLeft).TextTruncate().IsNotInteractable();
        if (badge is not null)
        {
            Pill(paper, theme, "badge", badge, theme.Warning);
        }

        var message = error ?? hint;
        return new RowScope(paper, outer, line, theme, message, error is not null ? theme.Error : theme.Warning);
    }

    public static void Message(Prowl.PaperUI.Paper paper, SettingsTheme theme, string id, string text, Color32 color)
    {
        ArgumentNullException.ThrowIfNull(paper);
        ArgumentNullException.ThrowIfNull(theme);

        using (paper.Row(id).Width(paper.Stretch()).Height(UnitValue.Auto).PaddingBottom(4).Enter())
        {
            paper.Box("spacer").Width(paper.Stretch()).Height(1).IsNotInteractable();
            paper.Box("text").Width(theme.ControlWidth).Height(UnitValue.Auto)
                .Text(text, theme.Font).FontSize(theme.Small).TextColor(color)
                .Wrap(TextWrapMode.Wrap).Alignment(TextAlignment.Left).IsNotInteractable();
        }
    }

    public static void Heading(Prowl.PaperUI.Paper paper, SettingsTheme theme, string id, string text)
    {
        ArgumentNullException.ThrowIfNull(paper);
        ArgumentNullException.ThrowIfNull(theme);

        paper.Box(id).Width(paper.Stretch()).Height(MathF.Round(theme.RowHeight * 1.2f))
            .Text(text, theme.Font).FontSize(MathF.Round(theme.FontSize * 1.15f)).TextColor(theme.Text)
            .Alignment(TextAlignment.BottomLeft).PaddingBottom(4).IsNotInteractable();
    }

    public static void Note(Prowl.PaperUI.Paper paper, SettingsTheme theme, string id, string text, Color32? color = null)
    {
        ArgumentNullException.ThrowIfNull(paper);
        ArgumentNullException.ThrowIfNull(theme);

        paper.Box(id).Width(paper.Stretch()).Height(UnitValue.Auto).PaddingLeft(14)
            .Text(text, theme.Font).FontSize(theme.Small).TextColor(color ?? theme.Dim)
            .Wrap(TextWrapMode.Wrap).Alignment(TextAlignment.Left).IsNotInteractable();
    }

    public static void Badge(Prowl.PaperUI.Paper paper, SettingsTheme theme, string id, string text) =>
        Pill(paper, theme, id, text, theme.Warning);

    public static bool Button(
        Prowl.PaperUI.Paper paper, SettingsTheme theme, string id, string text, Action onClick,
        bool enabled = true, bool primary = false)
    {
        ArgumentNullException.ThrowIfNull(paper);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(onClick);

        var fill = !enabled ? theme.Raised : primary ? theme.Accent : theme.Raised;
        var ink = !enabled ? theme.Dim : primary ? theme.AccentText : theme.Text;
        var builder = paper.Box(id).Width(UnitValue.Auto).Height(MathF.Round(theme.RowHeight * 0.8f))
            .Padding(12, 12, 0, 0).Rounded(5).BackgroundColor(fill).BorderColor(theme.Border).BorderWidth(primary ? 0 : 1)
            .Text(text, theme.Font).FontSize(theme.FontSize).TextColor(ink).Alignment(TextAlignment.MiddleCenter);
        if (!enabled)
        {
            builder.Cursor(PaperCursor.NotAllowed);
            return false;
        }

        builder.Cursor(PaperCursor.Pointer)
            .Hovered.BorderColor(theme.Accent).End()
            .OnClick(onClick, static (click, _) => click());
        return true;
    }

    public static void ToggleRow(
        Prowl.PaperUI.Paper paper, SettingsTheme theme, string id, string label, ISettingValue<bool> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        using var row = Row(paper, theme, id, label, value.IsDirty, value.Badge, value.Error, value.Hint);
        Toggle(paper, theme, "switch", value);
    }

    public static void Toggle(Prowl.PaperUI.Paper paper, SettingsTheme theme, string id, ISettingValue<bool> value)
    {
        ArgumentNullException.ThrowIfNull(paper);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(value);

        var on = value.Value;
        var height = MathF.Round(theme.FontSize * 1.5f);
        var width = MathF.Round(height * 1.8f);
        using (paper.Box(id).Size(width, height).Rounded(height / 2f)
            .BackgroundColor(on ? theme.Accent : theme.Raised).BorderColor(theme.Border).BorderWidth(on ? 0 : 1)
            .Cursor(PaperCursor.Pointer)
            .OnClick(value, static (setting, _) => setting.Value = !setting.Value)
            .Enter())
        {
            var knob = height - 4f;
            paper.Box("knob").PositionType(PositionType.SelfDirected)
                .Position(on ? width - knob - 2f : 2f, 2f).Size(knob, knob).Rounded(knob / 2f)
                .BackgroundColor(on ? theme.AccentText : theme.Dim).IsNotInteractable();
        }
    }

    public static void EnumRow(
        Prowl.PaperUI.Paper paper, SettingsTheme theme, string id, string label, ISettingValue<string> value,
        IReadOnlyList<string> choices)
    {
        ArgumentNullException.ThrowIfNull(paper);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(choices);

        using var row = Row(paper, theme, id, label, value.IsDirty, value.Badge, value.Error, value.Hint);
        var open = paper.GetElementStorage(row.Outer, "open", false);
        var current = value.Value;
        using (paper.Box("choice").Width(theme.ControlWidth).Height(MathF.Round(theme.RowHeight * 0.8f))
            .Rounded(5).BackgroundColor(theme.Raised).BorderColor(open ? theme.Accent : theme.Border).BorderWidth(1)
            .PaddingLeft(10).PaddingRight(10)
            .Text(current, theme.Font).FontSize(theme.FontSize)
            .TextColor(theme.Text).Alignment(TextAlignment.MiddleLeft)
            .Cursor(PaperCursor.Pointer)
            .OnClick((paper, row.Outer), static (context, _) =>
                context.paper.SetElementStorage(context.Outer, "open", !context.paper.GetElementStorage(context.Outer, "open", false)))
            .Enter())
        {
            var size = MathF.Round(theme.FontSize * 0.35f);
            var ink = theme.Dim;
            paper.DrawForeground((canvas, rect) =>
            {
                var x = (float)rect.Max.X - 12f - size;
                var y = (float)(rect.Min.Y + rect.Max.Y) / 2f;
                canvas.SetFillColor(ink);
                canvas.BeginPath();
                if (open)
                {
                    canvas.MoveTo(x, y + (size / 2f));
                    canvas.LineTo(x + size, y - (size / 2f));
                    canvas.LineTo(x + (size * 2f), y + (size / 2f));
                }
                else
                {
                    canvas.MoveTo(x, y - (size / 2f));
                    canvas.LineTo(x + size, y + (size / 2f));
                    canvas.LineTo(x + (size * 2f), y - (size / 2f));
                }

                canvas.ClosePath();
                canvas.Fill();
            });
        }

        row.EndLine();
        if (!open)
        {
            return;
        }

        using (paper.Column("choices").Width(paper.Stretch()).Height(UnitValue.Auto).PaddingLeft(14).PaddingBottom(6).Gap(2).Enter())
        {
            for (var i = 0; i < choices.Count; i++)
            {
                var choice = choices[i];
                var selected = string.Equals(choice, current, StringComparison.Ordinal);
                paper.Box("option", i).Width(paper.Stretch()).Height(MathF.Round(theme.RowHeight * 0.75f))
                    .Rounded(4).PaddingLeft(10).BackgroundColor(selected ? theme.Accent : theme.Surface)
                    .Hovered.BackgroundColor(selected ? theme.Accent : theme.Raised).End()
                    .Text(choice, theme.Font).FontSize(theme.FontSize)
                    .TextColor(selected ? theme.AccentText : theme.Text).Alignment(TextAlignment.MiddleLeft)
                    .Cursor(PaperCursor.Pointer)
                    .OnClick((paper, row.Outer, value, choice), static (context, _) =>
                    {
                        context.paper.SetElementStorage(context.Outer, "open", false);
                        context.value.Value = context.choice;
                    });
            }
        }
    }

    public static void SliderRow(
        Prowl.PaperUI.Paper paper, SettingsTheme theme, string id, string label, ISettingValue<double> value,
        double min, double max, double step, string format = "0.###")
    {
        ArgumentNullException.ThrowIfNull(paper);
        ArgumentNullException.ThrowIfNull(value);

        using var row = Row(paper, theme, id, label, value.IsDirty, value.Badge, value.Error, value.Hint);
        var current = value.Value;
        var span = max - min;
        var fraction = span <= 0 ? 0f : (float)Math.Clamp((current - min) / span, 0, 1);
        var numberWidth = MathF.Round(theme.FontSize * 5f);
        var trackWidth = theme.ControlWidth - numberWidth - 8f;
        var knob = MathF.Round(theme.FontSize * 1.1f);
        var slider = new Slider(value, min, max, step);
        using (paper.Box("track").Size(trackWidth, knob).Cursor(PaperCursor.Pointer)
            .OnPress(slider, static (s, e) => s.Set(e.NormalizedPosition.X))
            .OnDragging(slider, static (s, e) => s.Set(e.NormalizedPosition.X))
            .Enter())
        {
            var bar = MathF.Round(knob * 0.3f);
            paper.Box("rail").PositionType(PositionType.SelfDirected).Position(0, (knob - bar) / 2f)
                .Size(trackWidth, bar).Rounded(bar / 2f).BackgroundColor(theme.Raised).IsNotInteractable();
            paper.Box("fill").PositionType(PositionType.SelfDirected).Position(0, (knob - bar) / 2f)
                .Size(Math.Max(bar, trackWidth * fraction), bar).Rounded(bar / 2f).BackgroundColor(theme.Accent).IsNotInteractable();
            paper.Box("knob").PositionType(PositionType.SelfDirected).Position((trackWidth - knob) * fraction, 0)
                .Size(knob, knob).Rounded(knob / 2f).BackgroundColor(theme.Text).IsNotInteractable();
        }

        NumberField(paper, theme, "exact", numberWidth, value, format, min, max);
    }

    public static void NumberRow(
        Prowl.PaperUI.Paper paper, SettingsTheme theme, string id, string label, ISettingValue<double> value,
        string format = "0.###", double min = double.NegativeInfinity, double max = double.PositiveInfinity)
    {
        ArgumentNullException.ThrowIfNull(value);
        using var row = Row(paper, theme, id, label, value.IsDirty, value.Badge, value.Error, value.Hint);
        NumberField(paper, theme, "number", theme.ControlWidth, value, format, min, max);
    }

    public static void TextRow(
        Prowl.PaperUI.Paper paper, SettingsTheme theme, string id, string label, ISettingValue<string> value,
        string placeholder = "")
    {
        ArgumentNullException.ThrowIfNull(value);
        using var row = Row(paper, theme, id, label, value.IsDirty, value.Badge, value.Error, value.Hint);
        TextField(paper, theme, "text", theme.ControlWidth, value.Value, placeholder, value, static (setting, text) =>
        {
            setting.Value = text;
            return true;
        });
    }

    public static void ColorRow(
        Prowl.PaperUI.Paper paper, SettingsTheme theme, string id, string label, ISettingValue<string> value)
    {
        ArgumentNullException.ThrowIfNull(value);
        using var row = Row(paper, theme, id, label, value.IsDirty, value.Badge, value.Error, value.Hint);
        var current = value.Value;
        var valid = TryParseColor(current, out var parsed);
        var open = paper.GetElementStorage(row.Outer, "open", false);
        var swatch = MathF.Round(theme.RowHeight * 0.6f);
        paper.Box("swatch").Size(swatch, swatch).Rounded(4).BorderColor(open ? theme.Accent : theme.Border).BorderWidth(open ? 2 : 1)
            .BackgroundColor(valid ? parsed : new Color32(0, 0, 0, 0)).Cursor(PaperCursor.Pointer)
            .OnClick((paper, row.Outer), static (context, _) =>
                context.paper.SetElementStorage(context.Outer, "open", !context.paper.GetElementStorage(context.Outer, "open", false)));
        TextField(paper, theme, "hex", theme.ControlWidth - swatch - 8f, current, "#rrggbb", value, static (setting, text) =>
        {
            if (!TryParseColor(text, out _))
            {
                return false;
            }

            setting.Value = text.Trim();
            return true;
        });
        row.EndLine();
        if (open)
        {
            ColorPicker(paper, theme, row.Outer, value, valid ? parsed : new Color32(0x80, 0x80, 0x80, 0xff));
        }
    }

    public static string Hex(Color32 color) =>
        string.Create(CultureInfo.InvariantCulture, $"#{color.R:x2}{color.G:x2}{color.B:x2}");

    public static (float Hue, float Saturation, float Value) ToHsv(Color32 color)
    {
        var r = color.R / 255f;
        var g = color.G / 255f;
        var b = color.B / 255f;
        var max = MathF.Max(r, MathF.Max(g, b));
        var min = MathF.Min(r, MathF.Min(g, b));
        var delta = max - min;
        var hue = 0f;
        if (delta > 0)
        {
            hue = max == r ? (g - b) / delta % 6f : max == g ? ((b - r) / delta) + 2f : ((r - g) / delta) + 4f;
            hue /= 6f;
            if (hue < 0)
            {
                hue += 1f;
            }
        }

        return (hue, max <= 0 ? 0 : delta / max, max);
    }

    public static Color32 FromHsv(float hue, float saturation, float value)
    {
        hue = (hue - MathF.Floor(hue)) * 6f;
        saturation = Math.Clamp(saturation, 0f, 1f);
        value = Math.Clamp(value, 0f, 1f);
        var chroma = value * saturation;
        var x = chroma * (1f - MathF.Abs((hue % 2f) - 1f));
        var (r, g, b) = (int)hue switch
        {
            0 => (chroma, x, 0f),
            1 => (x, chroma, 0f),
            2 => (0f, chroma, x),
            3 => (0f, x, chroma),
            4 => (x, 0f, chroma),
            _ => (chroma, 0f, x),
        };
        var m = value - chroma;
        return new Color32(Channel(r + m), Channel(g + m), Channel(b + m), 0xff);
    }

    private static readonly Color32[] Presets =
    [
        new(0x1b, 0x1d, 0x23, 0xff), new(0x26, 0x2a, 0x3a, 0xff), new(0x2a, 0x35, 0xc0, 0xff), new(0x4c, 0x8d, 0xf6, 0xff),
        new(0x2e, 0xa0, 0x9a, 0xff), new(0x3f, 0xb9, 0x50, 0xff), new(0xe0, 0xa3, 0x3a, 0xff), new(0xe5, 0x5b, 0x5b, 0xff),
        new(0xc0, 0x4c, 0xd9, 0xff), new(0x9a, 0xa1, 0xae, 0xff), new(0xe6, 0xe9, 0xef, 0xff), new(0xff, 0xff, 0xff, 0xff),
    ];

    private static readonly Color32[] HueStops =
    [
        new(0xff, 0x00, 0x00, 0xff), new(0xff, 0xff, 0x00, 0xff), new(0x00, 0xff, 0x00, 0xff),
        new(0x00, 0xff, 0xff, 0xff), new(0x00, 0x00, 0xff, 0xff), new(0xff, 0x00, 0xff, 0xff), new(0xff, 0x00, 0x00, 0xff),
    ];

    private static void ColorPicker(
        Prowl.PaperUI.Paper paper, SettingsTheme theme, ElementHandle outer, ISettingValue<string> value, Color32 color)
    {
        var (hue, saturation, brightness) = ToHsv(color);
        var kept = paper.GetElementStorage(outer, "hue", -1f);
        if ((saturation <= 0f || brightness <= 0f) && kept >= 0f)
        {
            hue = kept;
        }

        var picker = new Picker(paper, outer, value, hue, saturation, brightness, theme.Text);
        var size = MathF.Round(theme.FontSize * 11f);
        var bar = MathF.Round(theme.FontSize * 1.4f);
        var dot = MathF.Round(theme.RowHeight * 0.6f);
        using (paper.Row("picker").Width(paper.Stretch()).Height(UnitValue.Auto).PaddingLeft(14).PaddingBottom(8).Gap(12).Enter())
        {
            using (paper.Box("sv").Size(size, size).Cursor(PaperCursor.Crosshair)
                .OnPress(picker, static (p, e) => p.Pick(e.NormalizedPosition.X, e.NormalizedPosition.Y))
                .OnDragging(picker, static (p, e) => p.Pick(e.NormalizedPosition.X, e.NormalizedPosition.Y))
                .Enter())
            {
                paper.Draw(picker.DrawSquare);
            }

            using (paper.Box("hue").Size(bar, size).Cursor(PaperCursor.ResizeVertical)
                .OnPress(picker, static (p, e) => p.PickHue(e.NormalizedPosition.Y))
                .OnDragging(picker, static (p, e) => p.PickHue(e.NormalizedPosition.Y))
                .Enter())
            {
                paper.Draw(picker.DrawHue);
            }

            using (paper.Column("presets").Width(UnitValue.Auto).Height(UnitValue.Auto).Gap(6).Enter())
            {
                for (var rowIndex = 0; rowIndex < 3; rowIndex++)
                {
                    using (paper.Row("presets-row", rowIndex).Width(UnitValue.Auto).Height(UnitValue.Auto).Gap(6).Enter())
                    {
                        for (var column = 0; column < 4; column++)
                        {
                            var preset = Presets[(rowIndex * 4) + column];
                            var chosen = preset.R == color.R && preset.G == color.G && preset.B == color.B;
                            paper.Box("preset", (rowIndex * 4) + column).Size(dot, dot).Rounded(4)
                                .BackgroundColor(preset).BorderColor(chosen ? theme.Accent : theme.Border).BorderWidth(chosen ? 2 : 1)
                                .Cursor(PaperCursor.Pointer)
                                .OnClick((picker, preset), static (context, _) => context.picker.Choose(context.preset));
                        }
                    }
                }
            }
        }
    }

    private static byte Channel(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);

    public static void ChordRow(
        Prowl.PaperUI.Paper paper, SettingsTheme theme, string id, string label, ISettingValue<string> value,
        ChordCapture capture)
    {
        ArgumentNullException.ThrowIfNull(value);
        using var row = Row(paper, theme, id, label, value.IsDirty, value.Badge, value.Error, value.Hint);
        ChordButton(paper, theme, "chord", theme.ControlWidth, value, capture);
    }

    public static void ChordButton(
        Prowl.PaperUI.Paper paper, SettingsTheme theme, string id, float width, ISettingValue<string> value,
        ChordCapture capture)
    {
        ArgumentNullException.ThrowIfNull(paper);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(capture);

        var capturing = ReferenceEquals(capture.Target, value);
        var text = capturing ? "Press a chord, Esc cancels" : value.Value;
        paper.Box(id).Width(width).Height(MathF.Round(theme.RowHeight * 0.8f)).Rounded(5)
            .BackgroundColor(capturing ? theme.Accent : theme.Raised).BorderColor(theme.Border).BorderWidth(capturing ? 0 : 1)
            .PaddingLeft(10).Text(text, theme.Font).FontSize(theme.FontSize)
            .TextColor(capturing ? theme.AccentText : theme.Text).Alignment(TextAlignment.MiddleLeft)
            .Cursor(PaperCursor.Pointer)
            .OnClick((capture, value), static (context, _) =>
            {
                if (ReferenceEquals(context.capture.Target, context.value))
                {
                    context.capture.Cancel();
                }
                else
                {
                    context.capture.Begin(context.value);
                }
            });
    }

    public static void ListRow<T>(
        Prowl.PaperUI.Paper paper, SettingsTheme theme, string id, string label, IList<T> items,
        Action<Prowl.PaperUI.Paper, int> buildItem, Func<T>? create, Action changed, bool dirty = false,
        string? badge = null, string? error = null, string? hint = null)
    {
        ArgumentNullException.ThrowIfNull(paper);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(buildItem);
        ArgumentNullException.ThrowIfNull(changed);

        using var row = Row(paper, theme, id, label, dirty, badge, error, hint);
        if (create is not null)
        {
            Button(paper, theme, "add", "Add", () =>
            {
                items.Add(create());
                changed();
            });
        }

        row.EndLine();
        using (paper.Column("items").Width(paper.Stretch()).Height(UnitValue.Auto).PaddingLeft(14).Gap(6).PaddingBottom(6).Enter())
        {
            for (var i = 0; i < items.Count; i++)
            {
                paper.PushID(i);
                using (paper.Row("item").Width(paper.Stretch()).Height(UnitValue.Auto).Gap(6).AlignItems(LayoutAlignment.Center)
                    .Padding(8).Rounded(6).BackgroundColor(theme.Surface).BorderColor(theme.Border).BorderWidth(1).Enter())
                {
                    using (paper.Column("body").Width(paper.Stretch()).Height(UnitValue.Auto).Gap(2).Enter())
                    {
                        buildItem(paper, i);
                    }

                    using (paper.Row("tools").Width(UnitValue.Auto).Height(UnitValue.Auto).Gap(4).Enter())
                    {
                        var index = i;
                        Button(paper, theme, "up", "Up", () => Swap(items, index, index - 1, changed), enabled: index > 0);
                        Button(paper, theme, "down", "Down", () => Swap(items, index, index + 1, changed), enabled: index < items.Count - 1);
                        Button(paper, theme, "remove", "Remove", () =>
                        {
                            items.RemoveAt(index);
                            changed();
                        });
                    }
                }

                paper.PopID();
            }
        }
    }

    public static void SectionNav(
        Prowl.PaperUI.Paper paper, SettingsTheme theme, string id, IReadOnlyList<string> sections, int selected,
        Action<int> select, IReadOnlyList<bool>? dirty = null)
    {
        ArgumentNullException.ThrowIfNull(paper);
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(select);

        using (paper.Column(id).Width(MathF.Round(theme.FontSize * 11f)).Height(paper.Stretch())
            .BackgroundColor(theme.Surface).Padding(8).Gap(2).Enter())
        {
            for (var i = 0; i < sections.Count; i++)
            {
                var active = i == selected;
                var mark = dirty is not null && i < dirty.Count && dirty[i];
                paper.Box("section", i).Width(paper.Stretch()).Height(MathF.Round(theme.RowHeight * 0.9f))
                    .Rounded(5).PaddingLeft(10).BackgroundColor(active ? theme.Accent : theme.Surface)
                    .Hovered.BackgroundColor(active ? theme.Accent : theme.Raised).End()
                    .Text(mark ? sections[i] + " •" : sections[i], theme.Font).FontSize(theme.FontSize)
                    .TextColor(active ? theme.AccentText : theme.Text).Alignment(TextAlignment.MiddleLeft)
                    .Cursor(PaperCursor.Pointer)
                    .OnClick((select, i), static (context, _) => context.select(context.i));
            }
        }
    }

    public static ScrollScope Scroll(Prowl.PaperUI.Paper paper, SettingsTheme theme, string id)
    {
        ArgumentNullException.ThrowIfNull(paper);
        ArgumentNullException.ThrowIfNull(theme);

        var outer = paper.Box(id).Width(paper.Stretch()).Height(paper.Stretch()).Clip();
        var outerScope = outer.Enter();
        var handle = paper.CurrentParent;
        outer.OnScroll((paper, handle), static (context, e) =>
        {
            var offset = context.paper.GetElementStorage(context.handle, "offset", 0f);
            var limit = context.paper.GetElementStorage(context.handle, "limit", 0f);
            offset = Math.Clamp(offset - (e.Delta * ScrollStep), 0f, Math.Max(0f, limit));
            context.paper.SetElementStorage(context.handle, "offset", offset);
        });
        outer.OnPostLayout((paper, handle), static (context, _, rect) =>
            context.paper.SetElementStorage(context.handle, "view", (float)rect.Size.Y));

        var scrolled = paper.GetElementStorage(handle, "offset", 0f);
        var content = paper.Column("content").PositionType(PositionType.SelfDirected).Position(0, -scrolled)
            .Width(paper.Stretch()).Height(UnitValue.Auto).PaddingRight(ScrollBarWidth + 10).PaddingLeft(16).PaddingTop(8).PaddingBottom(24).Gap(2)
            .OnPostLayout((paper, handle), static (context, _, rect) =>
            {
                var view = context.paper.GetElementStorage(context.handle, "view", 0f);
                var limit = Math.Max(0f, (float)rect.Size.Y - view);
                context.paper.SetElementStorage(context.handle, "limit", limit);
                context.paper.SetElementStorage(context.handle, "height", (float)rect.Size.Y);
                if (context.paper.GetElementStorage(context.handle, "offset", 0f) > limit)
                {
                    context.paper.SetElementStorage(context.handle, "offset", limit);
                }
            });
        return new ScrollScope(paper, theme, handle, outerScope, content.Enter(), scrolled);
    }

    public static bool TryParseColor(string? text, out Color32 color)
    {
        color = default;
        if (text is null)
        {
            return false;
        }

        var span = text.AsSpan().Trim();
        if (span.Length != 7 || span[0] != '#' ||
            !uint.TryParse(span[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return false;
        }

        color = new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 0xff);
        return true;
    }

    private static void NumberField(
        Prowl.PaperUI.Paper paper, SettingsTheme theme, string id, float width, ISettingValue<double> value,
        string format, double min, double max)
    {
        var shown = value.Value.ToString(format, CultureInfo.InvariantCulture);
        TextField(paper, theme, id, width, shown, string.Empty, (value, min, max), static (context, text) =>
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ||
                parsed < context.min || parsed > context.max)
            {
                return false;
            }

            context.value.Value = parsed;
            return true;
        });
    }

    private static void TextField<TContext>(
        Prowl.PaperUI.Paper paper, SettingsTheme theme, string id, float width, string value, string placeholder,
        TContext context, Func<TContext, string, bool> commit)
    {
        var settings = ElementBuilder.TextInputSettings.Default;
        settings.Font = theme.Font;
        settings.TextColor = theme.Text;
        settings.Placeholder = placeholder;
        settings.PlaceholderColor = theme.Dim;
        var field = new FieldCommit<TContext>(context, commit);
        using (paper.Box(id).Width(width).Height(MathF.Round(theme.RowHeight * 0.8f)).Rounded(5)
            .BackgroundColor(theme.Surface).BorderColor(theme.Border).BorderWidth(1)
            .Focused.BorderColor(theme.Accent).End()
            .Enter())
        {
            var handle = paper.CurrentParent;
            paper.Box("field").Margin(8, 8, UnitValue.StretchOne, UnitValue.StretchOne).Width(paper.Stretch())
                .Height(MathF.Round(theme.FontSize * 1.3f)).FontSize(theme.FontSize)
                .TextField(value, settings, text => paper.SetElementStorage(handle, "draft", text))
                .OnKeyPressed((field, paper, handle), static (c, e) =>
                {
                    if (e.Key is PaperKey.Enter or PaperKey.KeypadEnter)
                    {
                        c.field.Commit(c.paper, c.handle);
                    }
                })
                .OnFocusChange((field, paper, handle), static (c, e) =>
                {
                    if (!e.IsFocused)
                    {
                        c.field.Commit(c.paper, c.handle);
                    }
                });
        }
    }

    private static void Pill(Prowl.PaperUI.Paper paper, SettingsTheme theme, string id, string text, Color32 color)
    {
        paper.Box(id).Width(UnitValue.Auto).Height(MathF.Round(theme.Small * 1.6f)).Padding(6, 6, 0, 0).Rounded(4)
            .BackgroundColor(new Color32(color.R, color.G, color.B, 0x30))
            .Text(text, theme.Font).FontSize(theme.Small).TextColor(color).Alignment(TextAlignment.MiddleCenter)
            .IsNotInteractable();
    }

    private static void Swap<T>(IList<T> items, int a, int b, Action changed)
    {
        if (a < 0 || b < 0 || a >= items.Count || b >= items.Count)
        {
            return;
        }

        (items[a], items[b]) = (items[b], items[a]);
        changed();
    }

    public readonly struct RowScope : IDisposable
    {
        private readonly Prowl.PaperUI.Paper _paper;
        private readonly IDisposable _outer;
        private readonly LineScope _line;

        internal RowScope(
            Prowl.PaperUI.Paper paper, IDisposable outer, IDisposable line, SettingsTheme theme, string? message, Color32 color)
        {
            _paper = paper;
            _outer = outer;
            _line = new LineScope(paper, line, theme, message, color);
            Outer = paper.CurrentParent.GetParentHandle();
        }

        public ElementHandle Outer { get; }

        public void EndLine() => _line.Close();

        public void Dispose()
        {
            _line.Close();
            _outer.Dispose();
            _paper.PopID();
        }
    }

    public readonly struct ScrollScope : IDisposable
    {
        private readonly Prowl.PaperUI.Paper _paper;
        private readonly SettingsTheme _theme;
        private readonly ElementHandle _handle;
        private readonly IDisposable _outer;
        private readonly IDisposable _content;
        private readonly float _offset;

        internal ScrollScope(
            Prowl.PaperUI.Paper paper, SettingsTheme theme, ElementHandle handle, IDisposable outer, IDisposable content, float offset)
        {
            _paper = paper;
            _theme = theme;
            _handle = handle;
            _outer = outer;
            _content = content;
            _offset = offset;
        }

        public void Dispose()
        {
            _content.Dispose();
            var view = _paper.GetElementStorage(_handle, "view", 0f);
            var height = _paper.GetElementStorage(_handle, "height", 0f);
            if (height > view && view > 0)
            {
                var thumb = Math.Max(24f, view * view / height);
                var travel = view - thumb;
                var limit = height - view;
                var at = limit <= 0 ? 0 : travel * Math.Clamp(_offset / limit, 0f, 1f);
                _paper.Box("thumb").PositionType(PositionType.SelfDirected).Position(_paper.Percent(100, -ScrollBarWidth - 2), at)
                    .Size(ScrollBarWidth, thumb).Rounded(ScrollBarWidth / 2f)
                    .BackgroundColor(new Color32(_theme.Dim.R, _theme.Dim.G, _theme.Dim.B, 0x80)).IsNotInteractable();
            }

            _outer.Dispose();
        }
    }

    private sealed class Picker(
        Prowl.PaperUI.Paper paper, ElementHandle outer, ISettingValue<string> value,
        float hue, float saturation, float brightness, Color32 ink)
    {
        public void Pick(double x, double y)
        {
            saturation = (float)Math.Clamp(x, 0, 1);
            brightness = 1f - (float)Math.Clamp(y, 0, 1);
            Commit();
        }

        public void PickHue(double y)
        {
            hue = Math.Min((float)Math.Clamp(y, 0, 1), 0.9999f);
            Commit();
        }

        public void Choose(Color32 color)
        {
            (hue, saturation, brightness) = ToHsv(color);
            paper.SetElementStorage(outer, "hue", hue);
            value.Value = Hex(color);
        }

        public void DrawSquare(Canvas canvas, Rect rect)
        {
            var x = (float)rect.Min.X;
            var y = (float)rect.Min.Y;
            var w = (float)rect.Size.X;
            var h = (float)rect.Size.Y;
            canvas.RectFilled(x, y, w, h, FromHsv(hue, 1f, 1f));
            canvas.SetLinearBrush(x, y, x + w, y, new Color32(0xff, 0xff, 0xff, 0xff), new Color32(0xff, 0xff, 0xff, 0x00));
            canvas.BeginPath();
            canvas.Rect(x, y, w, h);
            canvas.Fill();
            canvas.SetLinearBrush(x, y, x, y + h, new Color32(0, 0, 0, 0x00), new Color32(0, 0, 0, 0xff));
            canvas.BeginPath();
            canvas.Rect(x, y, w, h);
            canvas.Fill();
            canvas.ClearBrush();
            Ring(canvas, x + (saturation * w), y + ((1f - brightness) * h));
        }

        public void DrawHue(Canvas canvas, Rect rect)
        {
            var x = (float)rect.Min.X;
            var y = (float)rect.Min.Y;
            var w = (float)rect.Size.X;
            var step = (float)rect.Size.Y / (HueStops.Length - 1);
            for (var i = 0; i < HueStops.Length - 1; i++)
            {
                var top = y + (i * step);
                canvas.SetLinearBrush(x, top, x, top + step, HueStops[i], HueStops[i + 1]);
                canvas.BeginPath();
                canvas.Rect(x, top, w, step + 0.5f);
                canvas.Fill();
            }

            canvas.ClearBrush();
            var at = y + (hue * (float)rect.Size.Y);
            canvas.SetStrokeColor(ink);
            canvas.SetStrokeWidth(2f);
            canvas.BeginPath();
            canvas.Rect(x - 1f, at - 2f, w + 2f, 4f);
            canvas.Stroke();
        }

        private void Ring(Canvas canvas, float cx, float cy)
        {
            canvas.SetStrokeWidth(2f);
            canvas.SetStrokeColor(new Color32(0, 0, 0, 0xc0));
            canvas.BeginPath();
            canvas.Circle(cx, cy, 7f);
            canvas.Stroke();
            canvas.SetStrokeColor(new Color32(0xff, 0xff, 0xff, 0xff));
            canvas.BeginPath();
            canvas.Circle(cx, cy, 5f);
            canvas.Stroke();
        }

        private void Commit()
        {
            paper.SetElementStorage(outer, "hue", hue);
            var hex = Hex(FromHsv(hue, saturation, brightness));
            if (!string.Equals(hex, value.Value, StringComparison.OrdinalIgnoreCase))
            {
                value.Value = hex;
            }
        }
    }

    private sealed class LineScope(
        Prowl.PaperUI.Paper paper, IDisposable line, SettingsTheme theme, string? message, Color32 color)
    {
        private IDisposable? _line = line;

        public void Close()
        {
            if (_line is null)
            {
                return;
            }

            _line.Dispose();
            _line = null;
            if (message is not null)
            {
                Message(paper, theme, "message", message, color);
            }
        }
    }

    private sealed class Slider(ISettingValue<double> value, double min, double max, double step)
    {
        public void Set(double fraction)
        {
            var raw = min + (Math.Clamp(fraction, 0, 1) * (max - min));
            var snapped = step > 0 ? min + (Math.Round((raw - min) / step) * step) : raw;
            snapped = Math.Clamp(Math.Round(snapped, 10), min, max);
            if (snapped != value.Value)
            {
                value.Value = snapped;
            }
        }
    }

    private sealed class FieldCommit<TContext>(TContext context, Func<TContext, string, bool> commit)
    {
        public void Commit(Prowl.PaperUI.Paper paper, ElementHandle handle)
        {
            if (paper.GetElementStorage<string?>(handle, "draft", null) is not { } draft)
            {
                return;
            }

            paper.SetElementStorage<string?>(handle, "draft", null);
            commit(context, draft);
        }
    }
}
