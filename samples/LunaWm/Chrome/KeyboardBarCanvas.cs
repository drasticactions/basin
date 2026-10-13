using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace LunaWm;

public sealed class KeyboardBarCanvas : Control
{
    private static readonly IBrush BarBackground = Brushes.Black;
    private static readonly IBrush BarBorder = new SolidColorBrush(Color.FromRgb(155, 155, 155));
    private static readonly Color Blue = Color.FromRgb(75, 151, 222);
    private static readonly Color White = Color.FromRgb(255, 255, 255);

    private LunaKeyboardSkin? _skin;

    public KeyboardModel? Model { get; set; }

    internal LunaKeyboardSkin? Skin
    {
        get => _skin;
        set
        {
            _skin = value;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        if (Model is not { Width: > 0 } model || _skin is null || model.BarHeight <= 0)
        {
            return;
        }

        context.FillRectangle(BarBackground, new Rect(0, KeyboardModel.PopupRoom, model.Width, model.BarHeight));
        DrawBar(context, model, _skin);
    }

    private static void DrawBar(DrawingContext context, KeyboardModel model, LunaKeyboardSkin skin)
    {
        var y = KeyboardModel.PopupRoom;
        var x = 15.0 - model.CandidateScroll;
        model.CandidateSpans.Clear();
        if (model.Preedit is { Length: > 0 } preedit)
        {
            var formatted = skin.Texts.Get(preedit, 24, White, FontWeight.SemiBold);
            var at = new Point(x, y + ((model.BarHeight - formatted.Height) / 2));
            context.DrawText(formatted, at);
            context.FillRectangle(Brushes.White, new Rect(x, at.Y + formatted.Height - 2, formatted.Width, 1.5));
            x += formatted.Width + 30;
        }

        for (var i = 0; i < model.Candidates.Count; i++)
        {
            var word = model.Candidates[i];
            var highlighted = i == model.CandidateCursor;
            var formatted = skin.Texts.Get(word, 24, highlighted ? Blue : White, FontWeight.SemiBold);
            if (x + formatted.Width >= 0 && x <= model.Width)
            {
                context.DrawText(formatted, new Point(x, y + ((model.BarHeight - formatted.Height) / 2)));
            }

            model.CandidateSpans.Add((x + model.CandidateScroll, x + model.CandidateScroll + formatted.Width));
            x += formatted.Width + 15;
            if (x >= 0 && x <= model.Width)
            {
                context.FillRectangle(BarBorder, new Rect(x, y + 8, 3, model.BarHeight - 16));
            }

            x += 18;
        }

        model.CandidateContentWidth = x + model.CandidateScroll;
    }
}
