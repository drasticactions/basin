using Avalonia.Controls;
using Basin.Hosted;

namespace Basin.Avalonia;

/// <summary>
/// Converts Avalonia screens into <see cref="HostScreenInfo"/> records and stable screen keys.
/// </summary>
public static class AvaloniaScreens
{
    /// <summary>
    /// Gets the stable key of a screen. The key is the display name, or <c>screen-N</c> by index when the name is empty.
    /// </summary>
    /// <param name="screens">The screen list that contains <paramref name="screen"/>.</param>
    /// <param name="screen">The screen, or <see langword="null"/>.</param>
    /// <returns>The key, or <see langword="null"/> when <paramref name="screen"/> is <see langword="null"/>.</returns>
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

    /// <summary>
    /// Makes a snapshot of every screen in host pixels, with the same keys that <see cref="KeyFor"/> gives.
    /// </summary>
    /// <param name="screens">The Avalonia screen list.</param>
    /// <returns>A new list in the order of <see cref="Screens.All"/>.</returns>
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
