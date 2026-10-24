using Basin.Cli;

namespace TabWm;

internal sealed record TabWmOptions
{
    public required BackendKind Backend { get; init; }

    public Basin.HandoffChoice Handoff { get; init; }

    public required string Renderer { get; init; }

    public int Outputs { get; init; } = 1;

    public double[] Scales { get; init; } = [];

    public long Frames { get; init; }

    public int SocketFd { get; init; } = -1;

    public string? ConfigPath { get; init; }

    public bool NoConfig { get; init; }

    public string? Screenshot { get; init; }

    public string? Engine { get; init; }

    public bool NoRestore { get; init; }

    public bool NoXWayland { get; init; }

    public string? Command { get; init; }

    public IpcChoice Ipc { get; init; } = IpcChoice.Off;
}
