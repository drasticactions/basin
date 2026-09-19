using Avalonia.Controls;
using Basin.Hosted;

namespace Basin.Avalonia;

public static class AvaloniaScreens
{
    public static string? KeyFor(Screens screens, global::Avalonia.Platform.Screen? screen)
    {
        ArgumentNullException.ThrowIfNull(screens);
        if (screen is null)
        {
            return null;
        }

        if (screen.DisplayName is { Length: > 0 } name)
        {
            return name;
        }

        var index = 0;
        for (; index < screens.All.Count; index++)
        {
            if (screens.All[index].Equals(screen))
            {
                break;
            }
        }

        return $"screen-{index}";
    }

    public static List<HostScreenInfo> Capture(Screens screens)
    {
        ArgumentNullException.ThrowIfNull(screens);
        var list = new List<HostScreenInfo>(screens.ScreenCount);
        for (var i = 0; i < screens.All.Count; i++)
        {
            var screen = screens.All[i];
            var key = screen.DisplayName is { Length: > 0 } name ? name : $"screen-{i}";
            list.Add(new HostScreenInfo(
                key,
                screen.DisplayName ?? key,
                screen.Bounds.X,
                screen.Bounds.Y,
                screen.Bounds.Width,
                screen.Bounds.Height,
                screen.Scaling,
                screen.IsPrimary));
        }

        return list;
    }
}
