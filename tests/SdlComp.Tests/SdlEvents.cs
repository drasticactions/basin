using SDL3;

namespace SdlComp.Tests;

internal static class SdlEvents
{
    public static void Key(uint window, SDL.Scancode scancode, bool down, bool repeat = false)
    {
        var e = default(SDL.Event);
        e.Key.Type = down ? SDL.EventType.KeyDown : SDL.EventType.KeyUp;
        e.Key.WindowID = window;
        e.Key.Scancode = scancode;
        e.Key.Down = down;
        e.Key.Repeat = repeat;
        Push(ref e);
    }

    public static void Motion(uint window, float x, float y)
    {
        var e = default(SDL.Event);
        e.Motion.Type = SDL.EventType.MouseMotion;
        e.Motion.WindowID = window;
        e.Motion.X = x;
        e.Motion.Y = y;
        Push(ref e);
    }

    public static void RelativeMotion(uint window, float dx, float dy)
    {
        var e = default(SDL.Event);
        e.Motion.Type = SDL.EventType.MouseMotion;
        e.Motion.WindowID = window;
        e.Motion.XRel = dx;
        e.Motion.YRel = dy;
        Push(ref e);
    }

    public static void Button(uint window, byte button, bool down, float x, float y)
    {
        var e = default(SDL.Event);
        e.Button.Type = down ? SDL.EventType.MouseButtonDown : SDL.EventType.MouseButtonUp;
        e.Button.WindowID = window;
        e.Button.Button = button;
        e.Button.Down = down;
        e.Button.X = x;
        e.Button.Y = y;
        Push(ref e);
    }

    public static void Wheel(uint window, float x, float y, bool flipped = false)
    {
        var e = default(SDL.Event);
        e.Wheel.Type = SDL.EventType.MouseWheel;
        e.Wheel.WindowID = window;
        e.Wheel.X = x;
        e.Wheel.Y = y;
        e.Wheel.Direction = flipped ? SDL.MouseWheelDirection.Flipped : SDL.MouseWheelDirection.Normal;
        Push(ref e);
    }

    public static void Window(uint window, SDL.EventType type)
    {
        var e = default(SDL.Event);
        e.Window.Type = type;
        e.Window.WindowID = window;
        Push(ref e);
    }

    public static void Drop(uint window, SDL.EventType type, float x = 0, float y = 0, string? data = null)
    {
        var e = default(SDL.Event);
        e.Drop.Type = type;
        e.Drop.WindowID = window;
        e.Drop.X = x;
        e.Drop.Y = y;
        e.Drop.Data = data is null ? 0 : System.Runtime.InteropServices.Marshal.StringToCoTaskMemUTF8(data);
        Push(ref e);
    }

    public static void Finger(uint window, SDL.EventType type, ulong finger, float x, float y)
    {
        var e = default(SDL.Event);
        e.TFinger.Type = type;
        e.TFinger.WindowID = window;
        e.TFinger.FingerID = finger;
        e.TFinger.X = x;
        e.TFinger.Y = y;
        Push(ref e);
    }

    public static void Pinch(uint window, SDL.EventType type, float scale)
    {
        var e = default(SDL.Event);
        e.Pinch.Type = type;
        e.Pinch.WindowID = window;
        e.Pinch.Scale = scale;
        Push(ref e);
    }

    public static void PenProximity(uint window, uint pen, bool inside)
    {
        var e = default(SDL.Event);
        e.PProximity.Type = inside ? SDL.EventType.PenProximityIn : SDL.EventType.PenProximityOut;
        e.PProximity.WindowID = window;
        e.PProximity.Which = pen;
        Push(ref e);
    }

    public static void PenMotion(uint window, uint pen, float x, float y)
    {
        var e = default(SDL.Event);
        e.PMotion.Type = SDL.EventType.PenMotion;
        e.PMotion.WindowID = window;
        e.PMotion.Which = pen;
        e.PMotion.X = x;
        e.PMotion.Y = y;
        Push(ref e);
    }

    public static void PenAxis(uint window, uint pen, SDL.PenAxis axis, float value)
    {
        var e = default(SDL.Event);
        e.PAxis.Type = SDL.EventType.PenAxis;
        e.PAxis.WindowID = window;
        e.PAxis.Which = pen;
        e.PAxis.Axis = axis;
        e.PAxis.Value = value;
        Push(ref e);
    }

    public static void PenTouch(uint window, uint pen, bool down, bool eraser = false)
    {
        var e = default(SDL.Event);
        e.PTouch.Type = down ? SDL.EventType.PenDown : SDL.EventType.PenUp;
        e.PTouch.WindowID = window;
        e.PTouch.Which = pen;
        e.PTouch.Down = down;
        e.PTouch.Eraser = eraser;
        Push(ref e);
    }

    public static void PenButton(uint window, uint pen, byte button, bool down)
    {
        var e = default(SDL.Event);
        e.PButton.Type = down ? SDL.EventType.PenButtonDown : SDL.EventType.PenButtonUp;
        e.PButton.WindowID = window;
        e.PButton.Which = pen;
        e.PButton.Button = button;
        e.PButton.Down = down;
        Push(ref e);
    }

    public static void TextEditing(uint window, string text, int start)
    {
        var e = default(SDL.Event);
        e.Edit.Type = SDL.EventType.TextEditing;
        e.Edit.WindowID = window;
        e.Edit.Text = System.Runtime.InteropServices.Marshal.StringToCoTaskMemUTF8(text);
        e.Edit.Start = start;
        e.Edit.Length = 0;
        Push(ref e);
    }

    public static void TextInput(uint window, string text)
    {
        var e = default(SDL.Event);
        e.Text.Type = SDL.EventType.TextInput;
        e.Text.WindowID = window;
        e.Text.Text = System.Runtime.InteropServices.Marshal.StringToCoTaskMemUTF8(text);
        Push(ref e);
    }

    private static void Push(ref SDL.Event e)
    {
        if (!SDL.PushEvent(ref e))
        {
            throw new InvalidOperationException($"SDL_PushEvent failed: {SDL.GetError()}");
        }
    }
}
