using Basin.Cli;

namespace SdlComp;

internal sealed record SdlCompOptions
{
    public long Frames { get; init; }

    public int Width { get; init; } = 1280;

    public int Height { get; init; } = 800;

    public string? Socket { get; init; }

    public string Renderer { get; init; } = Basin.Sdl.SdlRenderDevices.DefaultName;

    public string? Screenshot { get; init; }

    public IpcChoice Ipc { get; init; } = IpcChoice.Off;

    public double? Scale { get; init; }

    public string? VideoDriver { get; init; }

    public bool GrabKeyboard { get; init; }

    public int Outputs { get; init; } = 1;

    public string? WaypipeListen { get; init; }

    public string? HyperVVm { get; init; }

    public bool Toplevels { get; init; }
}
