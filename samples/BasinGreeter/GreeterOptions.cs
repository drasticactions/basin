using Basin.Cli;

namespace BasinGreeter;

internal sealed class GreeterOptions
{
    public BackendKind Backend { get; init; } = BackendKind.Headless;

    public Basin.HandoffChoice Handoff { get; init; }

    public string Renderer { get; init; } = "vulkan";

    public bool RendererGiven { get; init; }

    public int Outputs { get; init; } = 1;

    public double[] Scales { get; init; } = [];

    public long Frames { get; init; }

    public string? Screenshot { get; init; }

    public string? ConfigPath { get; init; } = GreeterConfig.DefaultPath;

    public string? StatePath { get; init; }

    public string? GreetdSocket { get; init; }

    public bool Demo { get; init; }

    public IpcChoice Ipc { get; init; } = IpcChoice.Off;

    public int SocketFd { get; init; } = -1;
}
