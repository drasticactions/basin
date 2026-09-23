using Avalonia.Controls;
using Basin;
using Basin.Scene;

namespace EightWm;

internal sealed class LaunchFlip
{
    public LaunchFlip(Tile tile, Control source, Box from, Box to, SceneTransform page, AppWindow? app)
    {
        Tile = tile;
        Source = source;
        From = from;
        To = to;
        Page = page;
        App = app;
        Revealing = app is not null;
    }

    public Tile Tile { get; }

    public Control Source { get; }

    public Box From { get; }

    public Box To { get; }

    public SceneTransform Page { get; }

    public bool Revealing { get; }

    public AppWindow? App { get; set; }

    public long StartMillis { get; set; }

    public bool BackShown { get; set; }

    public bool BackIsApp { get; set; }
}
