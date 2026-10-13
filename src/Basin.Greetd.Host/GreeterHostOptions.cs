using Avalonia;
using Basin.UI.Avalonia;

namespace Basin.Greetd.Host;

public sealed record GreeterHostOptions
{
    public required string Program { get; init; }

    public string Backend { get; init; } = "headless";

    public required string Renderer { get; init; }

    public int Outputs { get; init; } = 1;

    public IReadOnlyList<double> Scales { get; init; } = [];

    public long Frames { get; init; }

    public int SocketFd { get; init; } = -1;

    public OutputMode HeadlessMode { get; init; } = new(1280, 720, 60_000);

    public string? GreetdSocket { get; init; }

    public bool Demo { get; init; }

    public LoopbackGreetdOptions? DemoOptions { get; init; }

    public required GreeterSettings Settings { get; init; }

    public required GreeterMemory Memory { get; init; }

    public GreeterFlowOptions FlowOptions { get; init; } = new();

    public bool IpcListen { get; init; }

    public string? IpcPath { get; init; }

    public string CommandNamespace { get; init; } = "greeter";

    public UIThemeVariant Theme { get; init; } = UIThemeVariant.Dark;

    public Func<AppBuilder, AppBuilder>? Configure { get; init; }

    public bool ReaderChord { get; init; } = true;

    public bool IsDrm => Backend == "drm";
}
