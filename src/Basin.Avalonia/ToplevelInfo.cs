using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basin.Scene;
using Basin.Shell.Xdg;

namespace Basin.Avalonia;

public sealed record ToplevelInfo(
    string Title, string AppId, int Width, int Height, Wayland.Server.WlClient? Client = null);
