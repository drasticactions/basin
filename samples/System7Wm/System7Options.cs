using Basin.Cli;

namespace System7Wm;

internal sealed record System7Options
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

    public bool? XWayland { get; init; }

    public string? Screenshot { get; init; }

    public string? Command { get; init; }

    public IpcChoice Ipc { get; init; } = IpcChoice.Off;
}
