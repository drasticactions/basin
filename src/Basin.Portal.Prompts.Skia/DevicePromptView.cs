using Basin.Capabilities;
using SkiaSharp;

namespace Basin.Portal.Prompts.Skia;

/// <summary>
/// Asks the user which input devices a remote desktop session can control. It shows a checkbox for each requested device.
/// It can also show rows for clipboard sharing and for persistence.
/// </summary>
public sealed class DevicePromptView : SkiaPromptView
{
    private readonly List<(string Label, InputDeviceCapability Device)> _rows = [];
    private readonly bool _offerClipboard;
    private readonly bool _offerPersist;
    private int _hotRow = -1;

    /// <summary>
    /// Initializes a new instance of the <see cref="DevicePromptView"/> class. Each requested device and the clipboard start checked.
    /// </summary>
    /// <param name="theme">The theme to draw with.</param>
    /// <param name="prompt">The request that names the devices and the extra rows.</param>
    public DevicePromptView(SkiaPromptTheme theme, in DevicePrompt prompt)
        : base(theme, "Allow remote control?", SkiaPortalPrompts.AppLine(prompt.AppId, prompt.DisplayName), prompt.IconPath)
    {
        if ((prompt.Requested & InputDeviceCapability.Keyboard) != 0)
        {
            _rows.Add(("Keyboard", InputDeviceCapability.Keyboard));
        }

        if ((prompt.Requested & InputDeviceCapability.Pointer) != 0)
        {
            _rows.Add(("Pointer", InputDeviceCapability.Pointer));
        }

        if ((prompt.Requested & InputDeviceCapability.Touch) != 0)
        {
            _rows.Add(("Touchscreen", InputDeviceCapability.Touch));
        }

        Devices = prompt.Requested;
        _offerClipboard = prompt.ClipboardRequested;
        Clipboard = prompt.ClipboardRequested;
        _offerPersist = prompt.OfferPersist;
        AddButton("Cancel", PromptResponse.Denied, primary: false);
        AddButton("Allow", PromptResponse.Accepted, primary: true);
    }

    /// <summary>
    /// Gets the devices that are checked now.
    /// </summary>
    public InputDeviceCapability Devices { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the clipboard row is checked. It stays <see langword="false"/> when the request did not ask for the clipboard.
    /// </summary>
    public bool Clipboard { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the user asked to remember the choice.
    /// </summary>
    public bool Persist { get; private set; }

    private int ExtraRows => (_offerClipboard ? 1 : 0) + (_offerPersist ? 1 : 0);

    /// <summary>
    /// Gets the height of the dialog in logical pixels, from the number of rows.
    /// </summary>
    public override int Height => BodyTop + ((_rows.Count + ExtraRows) * RowHeight) + 16 + ButtonHeight + Padding + 12;

    /// <summary>
    /// Allows the Allow button only when at least one device is checked.
    /// </summary>
    /// <returns><see langword="true"/> when a device is checked.</returns>
    protected override bool CanAccept() => Devices != 0;

    /// <summary>
    /// Paints a checkbox row for each device and each extra row.
    /// </summary>
    /// <param name="canvas">The canvas to draw on.</param>
    /// <param name="width">The surface width in logical pixels.</param>
    /// <param name="height">The surface height in logical pixels.</param>
    protected override void PaintBody(SKCanvas canvas, int width, int height)
    {
        var y = BodyTop;
        for (var i = 0; i < _rows.Count + ExtraRows; i++)
        {
            if (_hotRow == i)
            {
                Theme.Fill.Color = Theme.RowHot;
                canvas.DrawRoundRect(new SKRect(Padding, y, width - Padding, y + RowHeight), 4, 4, Theme.Fill);
            }

            var (label, on) = RowState(i);
            DrawCheckbox(canvas, Padding + 8, y + 8, on);
            Theme.DrawText(canvas, label, Theme.BodyFont, Padding + 34, y + 21, Theme.Foreground, width - 2 * Padding - 40);
            y += RowHeight;
        }
    }

    private (string Label, bool On) RowState(int index)
    {
        if (index < _rows.Count)
        {
            return (_rows[index].Label, (Devices & _rows[index].Device) != 0);
        }

        index -= _rows.Count;
        if (_offerClipboard && index == 0)
        {
            return ("Share the clipboard", Clipboard);
        }

        return ("Remember this choice", Persist);
    }

    private void Toggle(int index)
    {
        if (index < _rows.Count)
        {
            Devices ^= _rows[index].Device;
            return;
        }

        index -= _rows.Count;
        if (_offerClipboard && index == 0)
        {
            Clipboard = !Clipboard;
            return;
        }

        Persist = !Persist;
    }

    private int RowAt(double x, double y)
    {
        if (x < Padding || x >= SurfaceWidth - Padding || y < BodyTop)
        {
            return -1;
        }

        var row = (int)((y - BodyTop) / RowHeight);
        return row < _rows.Count + ExtraRows ? row : -1;
    }

    /// <summary>
    /// Highlights the row under the pointer.
    /// </summary>
    /// <param name="x">The horizontal position in surface coordinates.</param>
    /// <param name="y">The vertical position in surface coordinates.</param>
    /// <returns><see langword="true"/> when the highlighted row changed.</returns>
    protected override bool BodyPointerMove(double x, double y)
    {
        var row = RowAt(x, y);
        var changed = row != _hotRow;
        _hotRow = row;
        return changed;
    }

    /// <summary>
    /// Toggles the row under the pointer.
    /// </summary>
    /// <param name="x">The horizontal position in surface coordinates.</param>
    /// <param name="y">The vertical position in surface coordinates.</param>
    /// <returns><see langword="true"/> when a row was toggled.</returns>
    protected override bool BodyPointerRelease(double x, double y)
    {
        var row = RowAt(x, y);
        if (row < 0)
        {
            return false;
        }

        Toggle(row);
        return true;
    }

    /// <summary>
    /// Toggles the highlighted row on Space.
    /// </summary>
    /// <param name="key">The evdev keycode.</param>
    /// <returns><see langword="true"/> when a row was toggled.</returns>
    protected override bool BodyKey(uint key)
    {
        if (key == InputCodes.KeySpace && _hotRow >= 0)
        {
            Toggle(_hotRow);
            return true;
        }

        return false;
    }
}
