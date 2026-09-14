using Basin.Capabilities;
using SkiaSharp;

namespace Basin.Portal.Prompts.Skia;

public sealed class DevicePromptView : SkiaPromptView
{
    private readonly List<(string Label, InputDeviceCapability Device)> _rows = [];
    private readonly bool _offerClipboard;
    private readonly bool _offerPersist;
    private int _hotRow = -1;

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

    public InputDeviceCapability Devices { get; private set; }

    public bool Clipboard { get; private set; }

    public bool Persist { get; private set; }

    private int ExtraRows => (_offerClipboard ? 1 : 0) + (_offerPersist ? 1 : 0);

    public override int Height => BodyTop + ((_rows.Count + ExtraRows) * RowHeight) + 16 + ButtonHeight + Padding + 12;

    protected override bool CanAccept() => Devices != 0;

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

    protected override bool BodyPointerMove(double x, double y)
    {
        var row = RowAt(x, y);
        var changed = row != _hotRow;
        _hotRow = row;
        return changed;
    }

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
