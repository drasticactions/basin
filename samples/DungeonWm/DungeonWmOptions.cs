using Basin.Cli;

namespace DungeonWm;

internal sealed record DungeonWmOptions
{
    public required BackendKind Backend { get; init; }

    public Basin.HandoffChoice Handoff { get; init; }

    public required string Renderer { get; init; }

    public required DungeonWmSettings Settings { get; init; }

    public int Outputs { get; init; } = 1;

    public int Width { get; init; } = 1280;

    public int Height { get; init; } = 720;

    public double[] Scales { get; init; } = [];

    public long Frames { get; init; }

    public int SocketFd { get; init; } = -1;

    public string? ConfigPath { get; init; }

    public bool NoConfig { get; init; }

    public bool XWayland { get; init; }

    public string? Screenshot { get; init; }

    public string? Command { get; init; }

    public IpcChoice Ipc { get; init; } = IpcChoice.Off;
}
