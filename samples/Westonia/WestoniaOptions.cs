using Basin.Cli;
using Basin.UI.Avalonia;

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
