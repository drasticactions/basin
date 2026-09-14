using Basin.Capabilities;
using SkiaSharp;

namespace Basin.Portal.Prompts.Skia;

public sealed class AreaPromptView : SkiaPromptView
{
    private readonly int _width;
    private readonly int _height;
    private readonly bool _pickPoint;
    private double _startX = -1;
    private double _startY = -1;
    private double _endX;
    private double _endY;
    private bool _dragging;

    public AreaPromptView(SkiaPromptTheme theme, in AreaPrompt prompt, int width, int height)
        : base(theme, prompt.PickPoint ? "Pick a colour" : "Select an area", SkiaPortalPrompts.AppLine(prompt.AppId, prompt.DisplayName), prompt.IconPath)
    {
        _width = width;
        _height = height;
        _pickPoint = prompt.PickPoint;
    }

    public override int SurfaceWidth => _width;

    public override int Height => _height;

    public Box Selection { get; private set; }

    public new void Paint(SKCanvas canvas, int width, int height)
    {
        canvas.Clear(Theme.Veil);
        var hint = _pickPoint ? "Click a pixel, Escape cancels" : "Drag to select, Escape cancels, Enter takes everything";
        var hintWidth = Theme.MeasureText(hint, Theme.BodyFont);
        Theme.Fill.Color = Theme.Panel;
        canvas.DrawRoundRect(new SKRect(width / 2f - hintWidth / 2 - 14, 16, width / 2f + hintWidth / 2 + 14, 48), 6, 6, Theme.Fill);
        Theme.DrawText(canvas, hint, Theme.BodyFont, width / 2f - hintWidth / 2, 37, Theme.Foreground);
        if (_dragging || (!Selection.IsEmpty && !_pickPoint))
        {
            var box = _dragging ? Normalize() : Selection;
            var rect = new SKRect(box.X, box.Y, box.X + box.Width, box.Y + box.Height);
            canvas.Save();
            canvas.ClipRect(rect, SKClipOperation.Difference);
            canvas.Restore();
            Theme.Fill.Color = Theme.Rubber;
            canvas.DrawRect(rect, Theme.Fill);
            Theme.Stroke.Color = Theme.ButtonPrimaryHot;
            canvas.DrawRect(rect, Theme.Stroke);
            var label = $"{box.Width} × {box.Height}";
            Theme.DrawText(canvas, label, Theme.SmallFont, rect.Left + 4, rect.Top - 4 < 12 ? rect.Bottom + 14 : rect.Top - 4, Theme.Foreground);
        }
    }

    protected override void PaintBody(SKCanvas canvas, int width, int height)
    {
    }

    public override string? CursorAt(double x, double y) => "crosshair";

    protected override bool BodyPointerMove(double x, double y)
    {
        if (!_dragging)
        {
            return false;
        }

        _endX = x;
        _endY = y;
        return true;
    }

    public override bool PointerButton(uint button, bool pressed)
    {
        if (button != InputCodes.BtnLeft || IsDone)
        {
            return false;
        }

        if (pressed)
        {
            _startX = _endX = PointerX;
            _startY = _endY = PointerY;
            _dragging = true;
            return true;
        }

        if (!_dragging)
        {
            return false;
        }

        _dragging = false;
        _endX = PointerX;
        _endY = PointerY;
        if (_pickPoint)
        {
            Selection = new Box((int)Math.Floor(_endX), (int)Math.Floor(_endY), 1, 1);
            Complete(PromptResponse.Accepted);
            return true;
        }

        var box = Normalize();
        if (box.Width < 2 || box.Height < 2)
        {
            Selection = default;
            return true;
        }

        Selection = box;
        Complete(PromptResponse.Accepted);
        return true;
    }

    public override bool Key(uint key, bool pressed)
    {
        if (!pressed || IsDone)
        {
            return false;
        }

        if (key == InputCodes.KeyEsc)
        {
            Complete(PromptResponse.Cancelled);
            return true;
        }

        if (key is InputCodes.KeyEnter or InputCodes.KeyKpEnter && !_pickPoint)
        {
            Selection = new Box(0, 0, _width, _height);
            Complete(PromptResponse.Accepted);
            return true;
        }

        return false;
    }

    private Box Normalize()
    {
        var x1 = (int)Math.Floor(Math.Min(_startX, _endX));
        var y1 = (int)Math.Floor(Math.Min(_startY, _endY));
        var x2 = (int)Math.Ceiling(Math.Max(_startX, _endX));
        var y2 = (int)Math.Ceiling(Math.Max(_startY, _endY));
        return new Box(Math.Max(0, x1), Math.Max(0, y1), Math.Min(_width, x2) - Math.Max(0, x1), Math.Min(_height, y2) - Math.Max(0, y1));
    }
}
