using Basin.Capabilities;
using Basin.Config;
using Basin.Icons.Skia;
using Basin.Render.Skia;
using SkiaSharp;

namespace Basin.Portal.Prompts.Skia;

/// <summary>
/// Base class for a portal prompt that Skia draws: a panel with an icon, a title, a body and a row of buttons.
/// The host sends input to it and paints it again when an input method returns <see langword="true"/>.
/// </summary>
public abstract class SkiaPromptView : IDisposable
{
    /// <summary>
    /// The default width of a dialog in logical pixels.
    /// </summary>
    public const int Width = 520;

    /// <summary>
    /// The margin around the dialog content in logical pixels.
    /// </summary>
    public const int Padding = 20;

    /// <summary>
    /// The height of one body row in logical pixels.
    /// </summary>
    public const int RowHeight = 32;

    /// <summary>
    /// The height of a button in logical pixels.
    /// </summary>
    public const int ButtonHeight = 32;

    /// <summary>
    /// The width of a button in logical pixels.
    /// </summary>
    public const int ButtonWidth = 110;

    /// <summary>
    /// The size of the application icon in logical pixels.
    /// </summary>
    public const int IconSize = 32;

    private static readonly SKSamplingOptions IconSampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);

    private readonly List<Button> _buttons = [];
    private double _pointerX = -1;
    private double _pointerY = -1;
    private int _pressedButton = -1;
    private SKImage? _icon;

    /// <summary>
    /// Initializes a new instance of the <see cref="SkiaPromptView"/> class and loads the application icon.
    /// </summary>
    /// <param name="theme">The theme to draw with. The caller keeps ownership.</param>
    /// <param name="title">The title of the dialog.</param>
    /// <param name="appLine">The line under the title that names the application.</param>
    /// <param name="iconPath">The path of the icon file, or empty for no icon.</param>
    protected SkiaPromptView(SkiaPromptTheme theme, string title, string appLine, string iconPath = "")
    {
        Theme = theme;
        Title = title;
        AppLine = appLine;
        _icon = IconRasterizer.Image(iconPath, IconSize);
    }

    /// <summary>
    /// Gets the theme that the view draws with.
    /// </summary>
    public SkiaPromptTheme Theme { get; }

    /// <summary>
    /// Gets the title of the dialog.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets the line under the title that names the application.
    /// </summary>
    public string AppLine { get; }

    /// <summary>
    /// Gets a value indicating whether the icon file loaded.
    /// </summary>
    public bool HasIcon => _icon is not null;

    /// <summary>
    /// Gets the modifiers that are held or latched, from the last <see cref="Modifiers"/> call.
    /// </summary>
    public Modifiers HeldModifiers { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the view completed. A completed view ignores buttons and keys.
    /// </summary>
    public bool IsDone { get; private set; }

    /// <summary>
    /// Occurs once, when the user picks a button or presses Escape. The argument is the response.
    /// </summary>
    public event Action<PromptResponse>? Completed;

    /// <summary>
    /// Gets the buttons in the order that <see cref="AddButton"/> added them.
    /// </summary>
    protected IReadOnlyList<Button> Buttons => _buttons;

    /// <summary>
    /// Gets the index of the button under the pointer, or -1.
    /// </summary>
    protected int HotButton { get; private set; } = -1;

    /// <summary>
    /// Gets the last horizontal pointer position, or -1 when the pointer is not over the view.
    /// </summary>
    protected double PointerX => _pointerX;

    /// <summary>
    /// Gets the last vertical pointer position, or -1 when the pointer is not over the view.
    /// </summary>
    protected double PointerY => _pointerY;

    /// <summary>
    /// Gets the height that the view needs, in logical pixels.
    /// </summary>
    public abstract int Height { get; }

    /// <summary>
    /// Gets the width that the view needs, in logical pixels. The default is <see cref="Width"/>.
    /// </summary>
    public virtual int SurfaceWidth => Width;

    /// <summary>
    /// Adds a button. Buttons are laid out from the right, so the last button added is the rightmost one.
    /// </summary>
    /// <param name="label">The button text.</param>
    /// <param name="response">The response that the button completes the view with.</param>
    /// <param name="primary"><see langword="true"/> for the button that Enter activates.</param>
    protected void AddButton(string label, PromptResponse response, bool primary) => _buttons.Add(new Button(label, response, primary));

    /// <summary>
    /// Paints the panel, the icon, the title, the body and the buttons. It also records the button boxes for hit tests.
    /// A view that covers an output can override it.
    /// </summary>
    /// <param name="canvas">The canvas to draw on.</param>
    /// <param name="width">The surface width in logical pixels.</param>
    /// <param name="height">The surface height in logical pixels.</param>
    public virtual void Paint(SKCanvas canvas, int width, int height)
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

    /// <summary>
    /// Paints the content between the title and the buttons.
    /// </summary>
    /// <param name="canvas">The canvas to draw on.</param>
    /// <param name="width">The surface width in logical pixels.</param>
    /// <param name="height">The surface height in logical pixels.</param>
    protected abstract void PaintBody(SKCanvas canvas, int width, int height);

    /// <summary>
    /// Gets the top of the body area in logical pixels.
    /// </summary>
    protected int BodyTop => Padding + 48;

    /// <summary>
    /// Gets the bottom of the body area for a surface height.
    /// </summary>
    /// <param name="height">The surface height in logical pixels.</param>
    /// <returns>The bottom edge in logical pixels.</returns>
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

    /// <summary>
    /// Gets the cursor shape for a position. The default is <see langword="null"/>, which means the default cursor.
    /// </summary>
    /// <param name="x">The horizontal position in surface coordinates.</param>
    /// <param name="y">The vertical position in surface coordinates.</param>
    /// <returns>A cursor shape name, or <see langword="null"/>.</returns>
    public virtual string? CursorAt(double x, double y) => null;

    /// <summary>
    /// Records the pointer position, updates the hot button and passes the move to <see cref="BodyPointerMove"/>.
    /// </summary>
    /// <param name="x">The horizontal position in surface coordinates.</param>
    /// <param name="y">The vertical position in surface coordinates.</param>
    /// <returns><see langword="true"/> when the view needs a repaint.</returns>
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

    /// <summary>
    /// Handles a pointer move in the body. The default does nothing.
    /// </summary>
    /// <param name="x">The horizontal position in surface coordinates.</param>
    /// <param name="y">The vertical position in surface coordinates.</param>
    /// <returns><see langword="true"/> when the view needs a repaint.</returns>
    protected virtual bool BodyPointerMove(double x, double y) => false;

    /// <summary>
    /// Handles the left button. A release over the pressed button activates it, and other presses and releases go to the body.
    /// </summary>
    /// <param name="button">The evdev button code. Only the left button has an effect.</param>
    /// <param name="pressed"><see langword="true"/> for a press.</param>
    /// <returns><see langword="true"/> when the view needs a repaint.</returns>
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

    /// <summary>
    /// Handles a left press in the body. The default does nothing.
    /// </summary>
    /// <param name="x">The horizontal position in surface coordinates.</param>
    /// <param name="y">The vertical position in surface coordinates.</param>
    /// <returns><see langword="true"/> when the view needs a repaint.</returns>
    protected virtual bool BodyPointerPress(double x, double y) => false;

    /// <summary>
    /// Handles a left release that did not activate a button. The default does nothing.
    /// </summary>
    /// <param name="x">The horizontal position in surface coordinates.</param>
    /// <param name="y">The vertical position in surface coordinates.</param>
    /// <returns><see langword="true"/> when the view needs a repaint.</returns>
    protected virtual bool BodyPointerRelease(double x, double y) => false;

    /// <summary>
    /// Handles a scroll. The default does nothing.
    /// </summary>
    /// <param name="dx">The horizontal scroll value.</param>
    /// <param name="dy">The vertical scroll value.</param>
    /// <returns><see langword="true"/> when the view needs a repaint.</returns>
    public virtual bool PointerAxis(double dx, double dy) => false;

    /// <summary>
    /// Forgets the pointer position and clears the hot button.
    /// </summary>
    /// <returns><see langword="true"/> when a button was hot and the view needs a repaint.</returns>
    public virtual bool PointerLeave()
    {
        _pointerX = _pointerY = -1;
        var changed = HotButton != -1;
        HotButton = -1;
        return changed;
    }

    /// <summary>
    /// Cancels on Escape and activates the primary button on Enter. Other keys go to <see cref="BodyKey"/>.
    /// </summary>
    /// <param name="key">The evdev keycode.</param>
    /// <param name="pressed"><see langword="true"/> for a press. A release has no effect.</param>
    /// <returns><see langword="true"/> when the key had an effect.</returns>
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

    /// <summary>
    /// Handles a key press that the base view does not use. The default does nothing.
    /// </summary>
    /// <param name="key">The evdev keycode.</param>
    /// <returns><see langword="true"/> when the key had an effect.</returns>
    protected virtual bool BodyKey(uint key) => false;

    /// <summary>
    /// Sets <see cref="HeldModifiers"/> from an XKB modifier state. Locked modifiers and the group are ignored.
    /// </summary>
    /// <param name="depressed">The mask of held modifiers.</param>
    /// <param name="latched">The mask of latched modifiers.</param>
    /// <param name="locked">The mask of locked modifiers. It is ignored.</param>
    /// <param name="group">The layout group. It is ignored.</param>
    public void Modifiers(uint depressed, uint latched, uint locked, uint group) =>
        HeldModifiers = (Modifiers)((depressed | latched) & (uint)(Config.Modifiers.Shift | Config.Modifiers.Ctrl | Config.Modifiers.Alt | Config.Modifiers.Mod3 | Config.Modifiers.Super | Config.Modifiers.Mod5));

    /// <summary>
    /// Completes the view with the response of a button. An accept is ignored while <see cref="CanAccept"/> returns <see langword="false"/>.
    /// </summary>
    /// <param name="button">The button to activate.</param>
    protected void Activate(Button button)
    {
        if (button.Response == PromptResponse.Accepted && !CanAccept())
        {
            return;
        }

        Complete(button.Response);
    }

    /// <summary>
    /// Tells whether an accept is allowed now. The default always allows it.
    /// </summary>
    /// <returns><see langword="true"/> when the view can complete with an accepted response.</returns>
    protected virtual bool CanAccept() => true;

    /// <summary>
    /// Marks the view as done and raises <see cref="Completed"/>. A second call does nothing.
    /// </summary>
    /// <param name="response">The response to report.</param>
    protected void Complete(PromptResponse response)
    {
        if (IsDone)
        {
            return;
        }

        IsDone = true;
        Completed?.Invoke(response);
    }

    /// <summary>
    /// Releases the icon image. It does not dispose the theme.
    /// </summary>
    public void Dispose()
    {
        SkiaCensus.Release(_icon);
        _icon = null;
    }

    /// <summary>
    /// Tests whether a point is inside a box. The right and bottom edges are outside.
    /// </summary>
    /// <param name="box">The box to test.</param>
    /// <param name="x">The horizontal position in surface coordinates.</param>
    /// <param name="y">The vertical position in surface coordinates.</param>
    /// <returns><see langword="true"/> when the point is inside.</returns>
    protected static bool Contains(in Box box, double x, double y) =>
        x >= box.X && y >= box.Y && x < box.X + box.Width && y < box.Y + box.Height;

    /// <summary>
    /// Draws a 16 by 16 pixel checkbox, with a check mark when it is on.
    /// </summary>
    /// <param name="canvas">The canvas to draw on.</param>
    /// <param name="x">The left edge in logical pixels.</param>
    /// <param name="y">The top edge in logical pixels.</param>
    /// <param name="on"><see langword="true"/> to draw the check mark.</param>
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

    /// <summary>
    /// Represents one dialog button.
    /// </summary>
    /// <param name="label">The button text.</param>
    /// <param name="response">The response that the button completes the view with.</param>
    /// <param name="primary">A value indicating whether Enter activates the button and it uses the primary colors.</param>
    public sealed class Button(string label, PromptResponse response, bool primary)
    {
        /// <summary>
        /// Gets the button text.
        /// </summary>
        public string Label { get; } = label;

        /// <summary>
        /// Gets the response that the button completes the view with.
        /// </summary>
        public PromptResponse Response { get; } = response;

        /// <summary>
        /// Gets a value indicating whether the button is the primary one.
        /// </summary>
        public bool Primary { get; } = primary;

        /// <summary>
        /// Gets or sets the box of the button in logical pixels. <see cref="Paint"/> sets it, and hit tests read it.
        /// </summary>
        public Box Box { get; set; }
    }
}
