using Avalonia.Input;
using Basin.Diagnostics;

namespace Basin.Avalonia;

internal struct BasinInputEvent
{
    public InputKind Kind;
    public int WindowId;
    public uint TimeMs;
    public double X;
    public double Y;
    public uint Code;
    public bool Pressed;
    public double DeltaX;
    public double DeltaY;
    public int TouchId;
}
