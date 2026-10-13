using Basin.Cli;

namespace EightWm;

internal sealed record EightGreeterOptions
{
    public required BackendKind Backend { get; init; }

    public required string Renderer { get; init; }

    public bool RendererGiven { get; init; }

    public int Outputs { get; init; } = 1;

    public double[] Scales { get; init; } = [];

    public long Frames { get; init; }

    public int SocketFd { get; init; } = -1;

    public string? ConfigPath { get; init; } = EightGreeterConfig.DefaultPath;

    public string? StatePath { get; init; }

    public string? GreetdSocket { get; init; }

    public bool Demo { get; init; }

    public string? Screenshot { get; init; }

    public IpcChoice Ipc { get; init; } = IpcChoice.Off;
}
