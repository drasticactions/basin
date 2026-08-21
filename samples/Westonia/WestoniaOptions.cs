using System.Diagnostics;
using Avalonia;
using Basin;
using Basin.Capabilities;
using Basin.Cli;
using Basin.Desktop;
using Basin.Diagnostics;
using Basin.Host;
using Basin.Renderers;
using Basin.Scene;
using Basin.Shell.Weston;
using Basin.Shell.Xdg;
using Basin.UI.Avalonia;
using Microsoft.Extensions.Logging;
using Wayland.Server;

namespace Westonia;

internal sealed record WestoniaOptions
{
    public required BackendKind Backend { get; init; }

    public required string Renderer { get; init; }

    public int Outputs { get; init; } = 1;

    public double[] Scales { get; init; } = [];

    public long Frames { get; init; }

    public int SocketFd { get; init; } = -1;

    public string? ConfigPath { get; init; }

    public bool XWayland { get; init; }

    public string? Screenshot { get; init; }

    public UIThemeVariant Theme { get; init; } = UIThemeVariant.Light;

    public bool NoConfig { get; init; }
}
