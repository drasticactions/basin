using Avalonia;
using Avalonia.Media;
using Basin.SoftKeyboard;
using Basin.SoftKeyboard.Avalonia;

namespace LunaWm;

public sealed class LunaKeyboardSkin : ISoftKeyboardSkin
{
    private static readonly FontFamily Family = new("fonts:AvaEnyo#Source Sans 3");
    private static readonly IBrush Background = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.FromRgb(162, 162, 162), 0), new GradientStop(Color.FromRgb(80, 80, 80), 1) },
    };

    private static readonly IBrush Shadow = new SolidColorBrush(Color.FromArgb(102, 0, 0, 0));
    private static readonly IBrush LetterFace = Gray(210);
    private static readonly IBrush PunctuationFace = Gray(169);
    private static readonly IBrush FunctionFace = Gray(99);
    private static readonly IBrush ShiftOnceFace = new SolidColorBrush(Color.FromRgb(131, 136, 139));
    private static readonly IBrush ShiftLockFace = new SolidColorBrush(Color.FromRgb(75, 140, 198));
    private static readonly IBrush PopupBody = new SolidColorBrush(Color.FromArgb(240, 34, 34, 34));
    private static readonly Pen PopupEdge = new(Gray(110), 1);
    private static readonly Color Active = Color.FromRgb(20, 20, 20);
    private static readonly Color ActiveBack = Color.FromRgb(226, 226, 226);
    private static readonly Color FunctionText = Color.FromRgb(210, 210, 210);
    private static readonly Color FunctionBack = Color.FromRgb(0, 0, 0);
    private static readonly Color Secondary = Color.FromRgb(100, 100, 100);
    private static readonly Color SecondaryBack = Color.FromRgb(200, 200, 200);
    private static readonly Color Blue = Color.FromRgb(75, 151, 222);
    private static readonly IBrush FunctionGlyph = new SolidColorBrush(FunctionText);
    private static readonly IBrush FunctionGlyphBack = new SolidColorBrush(FunctionBack);
    private static readonly Pen GlyphPen = new(new SolidColorBrush(FunctionText), 3.5, lineCap: PenLineCap.Round);
    private static readonly Pen GlyphBackPen = new(new SolidColorBrush(FunctionBack), 3.5, lineCap: PenLineCap.Round);

    private readonly KeyboardModel _model;

    public LunaKeyboardSkin(KeyboardModel model)
    {
        _model = model;
    }

    public SoftKeyTextCache Texts { get; } = new(Family);

    public void DrawBackground(DrawingContext context, Rect bounds)
    {
        if (_model is not { Width: > 0 } model)
        {
            return;
        }

        context.FillRectangle(Background, new Rect(0, model.KeyboardTop, model.Width, model.Padding + model.Engine.KeymapHeight));
    }

    public void DrawKey(DrawingContext context, in SoftKeyDraw key)
    {
        if (_model.Width > 0)
        {
            DrawKey(context, _model, key.Cell, key.Key, key.Bounds);
        }
    }

    public void DrawOverKeys(DrawingContext context, Rect bounds)
    {
        if (_model is { Width: > 0 } model && model.TraceAlpha > 0.01f && model.Engine.TracePath.Length > 1)
        {
            DrawTrace(context, model, model.KeymapTop);
        }
    }

    public void DrawPopup(DrawingContext context, in SoftKeyPopupDraw popup)
    {
        if (_model.Width > 0)
        {
            DrawPopup(context, _model, (float)popup.KeymapTop);
        }
    }

    private static void DrawTrace(DrawingContext context, KeyboardModel model, float top)
    {
        var path = model.Engine.TracePath;
        var stroke = new StreamGeometry();
        using (var g = stroke.Open())
        {
            g.BeginFigure(new Point(path[0].X, path[0].Y + top), false);
            for (var i = 1; i < path.Length; i++)
            {
                g.LineTo(new Point(path[i].X, path[i].Y + top));
            }

            g.EndFigure(false);
        }

        var color = Color.FromArgb((byte)(255 * Math.Clamp(model.TraceAlpha, 0, 1)), 75, 151, 222);
        context.DrawGeometry(null, new Pen(new SolidColorBrush(color), 6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), stroke);
    }

    internal double LabelSize(string text, double keyWidth, double keyHeight, bool function, bool twoLabels)
    {
        double size = text.Length == 1
            ? text[0] is '.' or ',' or ';' or ':' or '\'' or '"' ? 28 : 26
            : 22;
        if (function || twoLabels)
        {
            size = 24;
        }

        if (text.Length > 1 && !function)
        {
            while (size > 8 && Text(text, size, Active).Width + 16 > keyWidth)
            {
                size--;
            }
        }

        return Math.Min(size, Math.Floor((keyHeight + 1) / 2));
    }

    private void DrawKey(DrawingContext context, KeyboardModel model, int cell, KeyCell key, Rect cellRect)
    {
        var engine = model.Engine;
        var state = engine.State;
        var pressed = engine.IsPressed(cell);
        var face = new Rect(cellRect.X + 6, cellRect.Y + (pressed ? 8 : 5), Math.Max(1, cellRect.Width - 12), Math.Max(1, cellRect.Height - (pressed ? 14 : 11)));
        if (!pressed)
        {
            context.DrawRectangle(Shadow, null, new RoundedRect(face.Translate(new Vector(0, 2)), 7));
        }

        var plain = key.Plain!;
        var brush = key.Class switch
        {
            KeyClass.Letter => LetterFace,
            KeyClass.Punctuation or KeyClass.NumberRow => PunctuationFace,
            _ => FunctionFace,
        };
        if (plain.Function == KeyFunction.Shift)
        {
            brush = state.Shift == ShiftMode.Lock ? ShiftLockFace : state.ShiftActive || state.AutoCap ? ShiftOnceFace : FunctionFace;
        }

        context.DrawRectangle(brush, null, new RoundedRect(face, 7));
        var shown = engine.CapAt(cell) ?? plain;
        switch (plain.Function)
        {
            case KeyFunction.Shift:
                DrawGlyph(context, face, KeyboardGlyphs.ShiftSize, state.Shift == ShiftMode.Lock ? 2 : state.ShiftActive || state.AutoCap ? 1 : 0);
                return;
            case KeyFunction.Backspace:
                DrawBackspace(context, face);
                return;
            case KeyFunction.Hide:
                DrawHide(context, face);
                return;
            case KeyFunction.Return:
                DrawCorner(context, face, model.EnterLabel);
                return;
            case KeyFunction.Text:
                break;
            default:
                var label = plain.Function switch
                {
                    KeyFunction.Symbol => state.SymbolLock ? KeyboardLayout.LockedSymbolLabel : model.Layout?.SymbolLabel ?? "+ = [ ]",
                    KeyFunction.Tab => "Tab",
                    KeyFunction.Left => "←",
                    KeyFunction.Right => "→",
                    _ => plain.Label ?? plain.Name,
                };
                DrawCentered(context, face, label, LabelSize(label, face.Width, cellRect.Height, true, false), FunctionText, FunctionBack);
                return;
        }

        if (plain.IsSpace)
        {
            if (model.Candidates.Count > 0)
            {
                var best = model.Candidates[0];
                DrawCentered(context, face, best, LabelSize(best, face.Width, cellRect.Height, false, false), Secondary, SecondaryBack);
            }

            return;
        }

        var alternate = key.Alternate ?? plain;
        if (key.Class == KeyClass.NumberRow && !ReferenceEquals(alternate, plain) && alternate.Text != plain.Text)
        {
            var digitRight = alternate.IsDigit ? alternate : plain;
            var other = ReferenceEquals(digitRight, alternate) ? plain : alternate;
            var rightActive = ReferenceEquals(shown, digitRight) || shown.Text == digitRight.Text;
            var half = face.Width / 2;
            var size = LabelSize("0", half, cellRect.Height, false, true);
            DrawLabel(context, new Rect(face.X + half - 4, face.Y, half, face.Height), Display(digitRight, state), rightActive ? size : size * 0.75, rightActive);
            DrawLabel(context, new Rect(face.X + 4, face.Y, half, face.Height), Display(other, state), rightActive ? size * 0.75 : size, !rightActive);
        }
        else if (!plain.IsLetter && !ReferenceEquals(alternate, plain) && alternate.Text != plain.Text && key.Class != KeyClass.Letter)
        {
            var plainActive = ReferenceEquals(shown, plain) || shown.Text == plain.Text;
            var size = LabelSize(plain.Text, face.Width, cellRect.Height, false, true);
            var lower = Text(Display(plain, state), plainActive ? size : size * 0.75, plainActive ? Active : Secondary);
            var upper = Text(Display(alternate, state), plainActive ? size * 0.75 : size, plainActive ? Secondary : Active);
            DrawEmbossed(context, Display(plain, state), plainActive ? size : size * 0.75, plainActive,
                new Point(face.X + ((face.Width - lower.Width) / 2), face.Bottom - 10 - lower.Height + (lower.Height * 0.2)));
            DrawEmbossed(context, Display(alternate, state), plainActive ? size * 0.75 : size, !plainActive,
                new Point(face.X + ((face.Width - upper.Width) / 2), face.Y + 10 - (upper.Height * 0.2)));
        }
        else
        {
            var text = Display(shown, state);
            DrawCentered(context, face, text, LabelSize(text, face.Width, cellRect.Height, false, false), Active, ActiveBack);
        }

        if (key.LongPress is not null && !(plain.IsLetter && state.SymbolActive))
        {
            var dots = Text("…", 14, Active);
            context.DrawText(dots, new Point(face.Right - dots.Width - 5, face.Bottom - dots.Height));
        }
    }

    private static string Display(KeyCap cap, KeyboardState state) =>
        cap.Text.Length == 1 && state.Capital ? cap.UpperText : cap.Text;

    private void DrawLabel(DrawingContext context, Rect area, string text, double size, bool active)
    {
        var formatted = Text(text, size, active ? Active : Secondary);
        DrawEmbossed(context, text, size, active, new Point(area.X + ((area.Width - formatted.Width) / 2), area.Y + ((area.Height - formatted.Height) / 2)));
    }

    private void DrawEmbossed(DrawingContext context, string text, double size, bool active, Point at)
    {
        context.DrawText(Text(text, size, active ? ActiveBack : SecondaryBack), at + new Vector(0, 1));
        context.DrawText(Text(text, size, active ? Active : Secondary), at);
    }

    private void DrawCentered(DrawingContext context, Rect face, string text, double size, Color color, Color back)
    {
        var formatted = Text(text, size, color);
        var at = new Point(face.X + ((face.Width - formatted.Width) / 2), face.Y + ((face.Height - formatted.Height) / 2));
        context.DrawText(Text(text, size, back), at + new Vector(0, 1));
        context.DrawText(formatted, at);
    }

    private void DrawCorner(DrawingContext context, Rect face, string label)
    {
        var area = new Rect(face.Right - (face.Width * 0.85), face.Bottom - (face.Height * 0.8), face.Width * 0.85, face.Height * 0.8);
        var size = 24.0;
        while (size > 10 && Text(label, size, FunctionText).Width + 8 > area.Width)
        {
            size--;
        }

        var formatted = Text(label, size, FunctionText);
        var at = new Point(area.Right - formatted.Width - 8, area.Bottom - formatted.Height - 4);
        context.DrawText(Text(label, size, FunctionBack), at + new Vector(0, 1));
        context.DrawText(formatted, at);
    }

    private static void DrawGlyph(DrawingContext context, Rect face, Size size, int mode)
    {
        var origin = new Point(face.X + ((face.Width - size.Width) / 2), face.Y + ((face.Height - size.Height) / 2));
        using (context.PushTransform(Matrix.CreateTranslation(origin.X, origin.Y + 1)))
        {
            context.DrawGeometry(mode > 0 ? FunctionGlyphBack : null, mode > 0 ? null : new Pen(FunctionGlyphBack, 2.5), KeyboardGlyphs.Shift);
        }

        using (context.PushTransform(Matrix.CreateTranslation(origin.X, origin.Y)))
        {
            var fill = mode == 2 ? Brushes.White : FunctionGlyph;
            context.DrawGeometry(mode > 0 ? fill : null, mode > 0 ? null : new Pen(FunctionGlyph, 2.5), KeyboardGlyphs.Shift);
            if (mode == 2)
            {
                context.DrawGeometry(Brushes.White, null, KeyboardGlyphs.ShiftLockBar);
            }
        }
    }

    private static void DrawBackspace(DrawingContext context, Rect face)
    {
        var size = KeyboardGlyphs.BackspaceSize;
        var origin = new Point(face.X + ((face.Width - size.Width) / 2), face.Y + ((face.Height - size.Height) / 2));
        using (context.PushTransform(Matrix.CreateTranslation(origin.X, origin.Y + 1)))
        {
            context.DrawGeometry(FunctionGlyphBack, null, KeyboardGlyphs.Backspace);
        }

        using (context.PushTransform(Matrix.CreateTranslation(origin.X, origin.Y)))
        {
            context.DrawGeometry(FunctionGlyph, null, KeyboardGlyphs.Backspace);
            context.DrawGeometry(null, new Pen(FunctionGlyphBack, 3, lineCap: PenLineCap.Round), KeyboardGlyphs.BackspaceCross);
        }
    }

    private static void DrawHide(DrawingContext context, Rect face)
    {
        var size = KeyboardGlyphs.HideSize;
        var origin = new Point(face.X + ((face.Width - size.Width) / 2), face.Y + ((face.Height - size.Height) / 2));
        using (context.PushTransform(Matrix.CreateTranslation(origin.X, origin.Y + 1)))
        {
            context.DrawGeometry(FunctionGlyphBack, null, KeyboardGlyphs.HideBody);
            context.DrawGeometry(null, GlyphBackPen, KeyboardGlyphs.HideChevron);
        }

        using (context.PushTransform(Matrix.CreateTranslation(origin.X, origin.Y)))
        {
            context.DrawGeometry(FunctionGlyph, null, KeyboardGlyphs.HideBody);
            context.DrawGeometry(null, GlyphPen, KeyboardGlyphs.HideChevron);
        }
    }

    private void DrawPopup(DrawingContext context, KeyboardModel model, float top)
    {
        var engine = model.Engine;
        var geometry = engine.LongPressGeometry;
        var entries = engine.LongPressEntries!;
        var frame = geometry.Frame;
        var body = new Rect(frame.X, frame.Y + top, frame.Width, frame.Height - 20);
        context.DrawRectangle(PopupBody, PopupEdge, new RoundedRect(body, 10));
        var pointer = new StreamGeometry();
        using (var g = pointer.Open())
        {
            g.BeginFigure(new Point(geometry.PointerX - (LongPressFrame.PointerWidth / 2), body.Bottom - 1), true);
            g.LineTo(new Point(geometry.PointerX + (LongPressFrame.PointerWidth / 2), body.Bottom - 1));
            g.LineTo(new Point(geometry.PointerX, body.Bottom + 17));
            g.EndFigure(true);
        }

        context.DrawGeometry(PopupBody, null, pointer);
        var state = engine.State;
        for (var i = 0; i < entries.Count; i++)
        {
            var cell = geometry.CellRect(i);
            var highlighted = i == engine.LongPressHighlightIndex;
            var face = new Rect(cell.X + 4, cell.Y + top + (highlighted ? 6 : 3), cell.Width - 8, cell.Height - (highlighted ? 10 : 7));
            if (!highlighted)
            {
                context.DrawRectangle(Shadow, null, new RoundedRect(face.Translate(new Vector(0, 2)), 7));
            }

            context.DrawRectangle(LetterFace, null, new RoundedRect(face, 7));
            var entry = entries[i];
            var label = entry.Function switch
            {
                KeyFunction.Text => Display(entry, state),
                _ => entry.Label ?? entry.Name,
            };
            var size = label.Length >= 6 ? 14 : 22;
            if (entry.Function == KeyFunction.Size && entry.Size == model.Size)
            {
                var formatted = Text(label, size, Blue, bold: true);
                context.DrawText(formatted, new Point(face.X + ((face.Width - formatted.Width) / 2), face.Y + ((face.Height - formatted.Height) / 2)));
                continue;
            }

            DrawCentered(context, face, label, size, Active, ActiveBack);
        }
    }

    private FormattedText Text(string text, double size, Color color, bool bold = false) =>
        Texts.Get(text, size, color, bold ? FontWeight.Bold : FontWeight.SemiBold);

    private static SolidColorBrush Gray(byte level) => new(Color.FromRgb(level, level, level));
}
