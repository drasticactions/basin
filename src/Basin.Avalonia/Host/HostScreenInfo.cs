using Avalonia.Controls;
using Basin.Desktop;

namespace Basin.Avalonia;

public sealed record HostScreenInfo(
    string Key, string Name, int X, int Y, int Width, int Height, double Scaling, bool Primary);
