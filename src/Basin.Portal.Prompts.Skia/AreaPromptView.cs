using Basin.Capabilities;
using SkiaSharp;

namespace Basin.Portal.Prompts.Skia;

/// <summary>
/// Lets the user drag a rectangle on one output, or click one pixel for a color pick.
/// The view covers the whole output and has no buttons.
/// </summary>
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

    /// <summary>
    /// Initializes a new instance of the <see cref="AreaPromptView"/> class.
    /// </summary>
    /// <param name="theme">The theme to draw with.</param>
    /// <param name="prompt">The request. Its <see cref="AreaPrompt.PickPoint"/> selects a one-pixel pick.</param>
    /// <param name="width">The width of the output in logical pixels.</param>
    /// <param name="height">The height of the output in logical pixels.</param>
    public AreaPromptView(SkiaPromptTheme theme, in AreaPrompt prompt, int width, int height)
        : base(theme, prompt.PickPoint ? "Pick a color" : "Select an area", SkiaPortalPrompts.AppLine(prompt.AppId, prompt.DisplayName), prompt.IconPath)
    {
        _width = width;
        _height = height;
        _pickPoint = prompt.PickPoint;
    }

    /// <summary>
    /// Gets the width of the output that the view covers, in logical pixels.
    /// </summary>
    public override int SurfaceWidth => _width;

    /// <summary>
    /// Gets the height of the output that the view covers, in logical pixels.
    /// </summary>
    public override int Height => _height;

    /// <summary>
    /// Gets the selected area in output-local logical pixels. It is a 1 by 1 box for a pick, the whole output after Enter, and empty before a selection.
    /// </summary>
    public Box Selection { get; private set; }

    /// <summary>
    /// Paints the veil, the hint text and the current selection rectangle.
    /// This method hides <see cref="SkiaPromptView.Paint"/>, so a call through a <see cref="SkiaPromptView"/> reference paints the base panel instead.
    /// </summary>
    /// <param name="canvas">The canvas to draw on.</param>
    /// <param name="width">The surface width in logical pixels.</param>
    /// <param name="height">The surface height in logical pixels.</param>
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

    /// <summary>
    /// Paints nothing. The area view draws everything in <see cref="Paint"/>.
    /// </summary>
    /// <param name="canvas">The canvas to draw on.</param>
    /// <param name="width">The surface width in logical pixels.</param>
    /// <param name="height">The surface height in logical pixels.</param>
    protected override void PaintBody(SKCanvas canvas, int width, int height)
    {
    }

    /// <summary>
    /// Gets the cursor shape for a position. It is always <c>crosshair</c>.
    /// </summary>
    /// <param name="x">The horizontal position in surface coordinates.</param>
    /// <param name="y">The vertical position in surface coordinates.</param>
    /// <returns>The cursor shape name <c>crosshair</c>.</returns>
    public override string? CursorAt(double x, double y) => "crosshair";

    /// <summary>
    /// Extends the selection while a drag is in progress.
    /// </summary>
    /// <param name="x">The horizontal position in surface coordinates.</param>
    /// <param name="y">The vertical position in surface coordinates.</param>
    /// <returns><see langword="true"/> when a drag is in progress and the view needs a repaint.</returns>
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

    /// <summary>
    /// Starts a drag on a left press and ends it on the release. The release completes the prompt with an accepted response.
    /// A selection smaller than 2 by 2 pixels is dropped and the prompt stays open.
    /// </summary>
    /// <param name="button">The evdev button code. Only the left button has an effect.</param>
    /// <param name="pressed"><see langword="true"/> for a press.</param>
    /// <returns><see langword="true"/> when the view needs a repaint.</returns>
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

    /// <summary>
    /// Cancels the prompt on Escape. Enter selects the whole output and accepts, except in a color pick.
    /// </summary>
    /// <param name="key">The evdev keycode.</param>
    /// <param name="pressed"><see langword="true"/> for a press. A release has no effect.</param>
    /// <returns><see langword="true"/> when the key had an effect.</returns>
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
