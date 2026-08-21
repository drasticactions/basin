using Basin.Cli;

namespace EightWm;

internal sealed record ShellOptions
{
    public required BackendKind Backend { get; init; }

    public required string Renderer { get; init; }

    public int Outputs { get; init; } = 1;

    public double[] Scales { get; init; } = [];

    public long Frames { get; init; }

    public string? Screenshot { get; init; }

    public string? Client { get; init; }

    public string? ConfigPath { get; init; }

    public bool HotCorners { get; init; } = true;

    public bool Animations { get; init; } = true;

    public int StartOutput { get; init; }

    public int MinWidth { get; init; } = 500;

    public double EdgeBand { get; init; } = 20;

    public bool XWayland { get; init; } = true;

    public int SocketFd { get; init; } = -1;

    public HashSet<string> Explicit { get; init; } = [];
}
