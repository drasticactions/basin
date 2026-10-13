using System.Globalization;

namespace Westonia;

public sealed record WestonLauncher(string Icon, string Path, string? DisplayName, Basin.X11Density? X11 = null);
