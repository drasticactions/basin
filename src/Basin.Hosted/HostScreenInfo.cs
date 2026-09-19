namespace Basin.Hosted;

public sealed record HostScreenInfo(
    string Key, string Name, int X, int Y, int Width, int Height, double Scaling, bool Primary);
