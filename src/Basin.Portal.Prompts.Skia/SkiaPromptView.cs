using Basin.Capabilities;
using Basin.Config;
using Basin.Render.Skia;
using SkiaSharp;

namespace Basin.Portal.Prompts.Skia;

public abstract class SkiaPromptView : IDisposable
{
    public const int Width = 520;

    public const int Padding = 20;

    public const int RowHeight = 32;

    public const int ButtonHeight = 32;

    public const int ButtonWidth = 110;

    public const int IconSize = 32;

    private static readonly SKSamplingOptions IconSampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    private readonly List<Button> _buttons = [];
    private double _pointerX = -1;
    private double _pointerY = -1;
    private int _pressedButton = -1;
    private SKImage? _icon;

    protected SkiaPromptView(SkiaPromptTheme theme, string title, string appLine, string iconPath = "")
    {
        Theme = theme;
        Title = title;
        AppLine = appLine;
        if (PromptIcon.Rasterize(iconPath, IconSize) is { } icon)
        {
            _icon = SkiaCensus.Track(icon);
        }
    }

    public SkiaPromptTheme Theme { get; }

    public string Title { get; }

    public string AppLine { get; }

    public bool HasIcon => _icon is not null;

    public Modifiers HeldModifiers { get; private set; }

    public bool IsDone { get; private set; }

    public event Action<PromptResponse>? Completed;

    protected IReadOnlyList<Button> Buttons => _buttons;

    protected int HotButton { get; private set; } = -1;

    protected double PointerX => _pointerX;

    protected double PointerY => _pointerY;

    public abstract int Height { get; }

    public virtual int SurfaceWidth => Width;

    protected void AddButton(string label, PromptResponse response, bool primary) => _buttons.Add(new Button(label, response, primary));

    public void Paint(SKCanvas canvas, int width, int height)
    {
        canvas.Clear(SKColors.Transparent);
        Theme.Fill.Color = Theme.Panel;
        canvas.DrawRoundRect(new SKRect(0, 0, width, height), 8, 8, Theme.Fill);
        Theme.Stroke.Color = Theme.Outline;
        canvas.DrawRoundRect(new SKRect(0.5f, 0.5f, width - 0.5f, height - 0.5f), 8, 8, Theme.Stroke);
        var textLeft = Padding;
        if (_icon is { } icon)
        {
            canvas.DrawImage(icon, new SKRect(Padding, Padding, Padding + IconSize, Padding + IconSize), IconSampling);
            textLeft += IconSize + 12;
        }

        Theme.DrawText(canvas, Title, Theme.TitleFont, textLeft, Padding + 16, Theme.Foreground, width - Padding - textLeft);
        Theme.DrawText(canvas, AppLine, Theme.SmallFont, textLeft, Padding + 36, Theme.Muted, width - Padding - textLeft);
        PaintBody(canvas, width, height);
        PaintButtons(canvas, width, height);
    }

    protected abstract void PaintBody(SKCanvas canvas, int width, int height);

    protected int BodyTop => Padding + 48;

    protected int BodyBottom(int height) => height - Padding - ButtonHeight - 12;

    private void PaintButtons(SKCanvas canvas, int width, int height)
    {
        var y = height - Padding - ButtonHeight;
        var x = width - Padding;
        for (var i = _buttons.Count - 1; i >= 0; i--)
        {
            var button = _buttons[i];
            x -= ButtonWidth;
            var box = new SKRect(x, y, x + ButtonWidth, y + ButtonHeight);
            button.Box = new Box((int)box.Left, (int)box.Top, ButtonWidth, ButtonHeight);
            var hot = HotButton == i;
            Theme.Fill.Color = button.Primary
                ? (hot ? Theme.ButtonPrimaryHot : Theme.ButtonPrimary)
                : (hot ? Theme.ButtonSecondaryHot : Theme.ButtonSecondary);
            canvas.DrawRoundRect(box, 5, 5, Theme.Fill);
            var textWidth = Theme.MeasureText(button.Label, Theme.BodyFont);
            Theme.DrawText(canvas, button.Label, Theme.BodyFont, box.MidX - textWidth / 2, box.MidY + 5, Theme.Foreground);
            x -= 10;
        }
    }

    public virtual string? CursorAt(double x, double y) => null;

    public virtual bool PointerMove(double x, double y)
    {
        _pointerX = x;
        _pointerY = y;
        var hot = -1;
        for (var i = 0; i < _buttons.Count; i++)
        {
            if (Contains(_buttons[i].Box, x, y))
            {
                hot = i;
            }
        }

        var changed = hot != HotButton;
        HotButton = hot;
        return BodyPointerMove(x, y) | changed;
    }

    protected virtual bool BodyPointerMove(double x, double y) => false;

    public virtual bool PointerButton(uint button, bool pressed)
    {
        if (button != InputCodes.BtnLeft || IsDone)
        {
            return false;
        }

        if (pressed)
        {
            _pressedButton = HotButton;
            return BodyPointerPress(_pointerX, _pointerY);
        }

        var released = _pressedButton;
        _pressedButton = -1;
        if (released >= 0 && released == HotButton)
        {
            Activate(_buttons[released]);
            return true;
        }

        return BodyPointerRelease(_pointerX, _pointerY);
    }

    protected virtual bool BodyPointerPress(double x, double y) => false;

    protected virtual bool BodyPointerRelease(double x, double y) => false;

    public virtual bool PointerAxis(double dx, double dy) => false;

    public virtual bool PointerLeave()
    {
        _pointerX = _pointerY = -1;
        var changed = HotButton != -1;
        HotButton = -1;
        return changed;
    }

    public virtual bool Key(uint key, bool pressed)
    {
        if (!pressed || IsDone)
        {
            return false;
        }

        switch (key)
        {
            case InputCodes.KeyEsc:
                Complete(PromptResponse.Cancelled);
                return true;
            case InputCodes.KeyEnter:
            case InputCodes.KeyKpEnter:
                foreach (var button in _buttons)
                {
                    if (button.Primary)
                    {
                        Activate(button);
                        return true;
                    }
                }

                return false;
            default:
                return BodyKey(key);
        }
    }

    protected virtual bool BodyKey(uint key) => false;

    public void Modifiers(uint depressed, uint latched, uint locked, uint group) =>
        HeldModifiers = (Modifiers)((depressed | latched) & (uint)(Config.Modifiers.Shift | Config.Modifiers.Ctrl | Config.Modifiers.Alt | Config.Modifiers.Mod3 | Config.Modifiers.Super | Config.Modifiers.Mod5));

    protected void Activate(Button button)
    {
        if (button.Response == PromptResponse.Accepted && !CanAccept())
        {
            return;
        }

        Complete(button.Response);
    }

    protected virtual bool CanAccept() => true;

    protected void Complete(PromptResponse response)
    {
        if (IsDone)
        {
            return;
        }

        IsDone = true;
        Completed?.Invoke(response);
    }

    public void Dispose()
    {
        SkiaCensus.Release(_icon);
        _icon = null;
    }

    protected static bool Contains(in Box box, double x, double y) =>
        x >= box.X && y >= box.Y && x < box.X + box.Width && y < box.Y + box.Height;

    protected void DrawCheckbox(SKCanvas canvas, float x, float y, bool on)
    {
        var box = new SKRect(x, y, x + 16, y + 16);
        Theme.Fill.Color = on ? Theme.Accent : Theme.ButtonSecondary;
        canvas.DrawRoundRect(box, 3, 3, Theme.Fill);
        Theme.Stroke.Color = Theme.Outline;
        canvas.DrawRoundRect(box, 3, 3, Theme.Stroke);
        if (on)
        {
            Theme.Stroke.Color = Theme.Foreground;
            Theme.Stroke.StrokeWidth = 2;
            canvas.DrawLine(x + 3.5f, y + 8, x + 7, y + 12, Theme.Stroke);
            canvas.DrawLine(x + 7, y + 12, x + 13, y + 4.5f, Theme.Stroke);
            Theme.Stroke.StrokeWidth = 1;
        }
    }

    public sealed class Button(string label, PromptResponse response, bool primary)
    {
        public string Label { get; } = label;

        public PromptResponse Response { get; } = response;

        public bool Primary { get; } = primary;

        public Box Box { get; set; }
    }
}
