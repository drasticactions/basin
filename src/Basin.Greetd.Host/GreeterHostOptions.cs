using Avalonia;
using Basin.UI.Avalonia;

namespace Basin.Greetd.Host;

/// <summary>
/// The options for a <see cref="GreeterHost"/>, from the program's command line and settings.
/// </summary>
public sealed record GreeterHostOptions
{
    /// <summary>
    /// Gets the program name, which names the nested output windows and the control socket session.
    /// </summary>
    public required string Program { get; init; }

    /// <summary>
    /// Gets the backend name, for example <c>headless</c>, <c>nested</c> or <c>drm</c>.
    /// </summary>
    public string Backend { get; init; } = "headless";

    /// <summary>
    /// Gets the name of the renderer to try first. The host falls back when it cannot start.
    /// </summary>
    public required string Renderer { get; init; }

    /// <summary>
    /// Gets the number of outputs to create on a headless or nested backend.
    /// </summary>
    public int Outputs { get; init; } = 1;

    /// <summary>
    /// Gets the scale of each created output, in output order. Empty uses the default scale.
    /// </summary>
    public IReadOnlyList<double> Scales { get; init; } = [];

    /// <summary>
    /// Gets the number of frames to run before the loop stops. Zero runs until a stop, and a positive value repaints every frame.
    /// </summary>
    public long Frames { get; init; }

    /// <summary>
    /// Gets an inherited Wayland listening socket fd, or -1 to create a socket.
    /// </summary>
    public int SocketFd { get; init; } = -1;

    /// <summary>
    /// Gets the handoff choice from the command line. It wins over the <c>[handoff]</c> settings.
    /// </summary>
    public HandoffChoice Handoff { get; init; }

    /// <summary>
    /// Gets the mode of each headless output. The default is 1280 by 720 at 60 Hz.
    /// </summary>
    public OutputMode HeadlessMode { get; init; } = new(1280, 720, 60_000);

    /// <summary>
    /// Gets the greetd socket path, or <see langword="null"/> to use <c>GREETD_SOCK</c>.
    /// </summary>
    public string? GreetdSocket { get; init; }

    /// <summary>
    /// Gets a value indicating whether the host serves a <see cref="LoopbackGreetd"/> instead of greetd. The host refuses it when <c>GREETD_SOCK</c> is set.
    /// </summary>
    public bool Demo { get; init; }

    /// <summary>
    /// Gets the conversation of the demo server, or <see langword="null"/> for the defaults.
    /// </summary>
    public LoopbackGreetdOptions? DemoOptions { get; init; }

    /// <summary>
    /// Gets the shared greeter settings.
    /// </summary>
    public required GreeterSettings Settings { get; init; }

    /// <summary>
    /// Gets the memory of the last user and of each user's choices.
    /// </summary>
    public required GreeterMemory Memory { get; init; }

    /// <summary>
    /// Gets the options of the <see cref="GreeterFlow"/> that <see cref="GreeterHost.StartLogin"/> creates.
    /// </summary>
    public GreeterFlowOptions FlowOptions { get; init; } = new();

    /// <summary>
    /// Gets a value indicating whether the control socket listens for connections.
    /// </summary>
    public bool IpcListen { get; init; }

    /// <summary>
    /// Gets the path of the control socket, or <see langword="null"/> for the default path.
    /// </summary>
    public string? IpcPath { get; init; }

    /// <summary>
    /// Gets the namespace of the host's own control methods, for example <c>greeter</c> for <c>greeter/type</c>.
    /// </summary>
    public string CommandNamespace { get; init; } = "greeter";

    /// <summary>
    /// Gets the theme variant of the Avalonia platform.
    /// </summary>
    public UIThemeVariant Theme { get; init; } = UIThemeVariant.Dark;

    /// <summary>
    /// Gets an extra step for the Avalonia app builder, or <see langword="null"/>.
    /// </summary>
    public Func<AppBuilder, AppBuilder>? Configure { get; init; }

    /// <summary>
    /// Gets a value indicating whether Super+Alt+S toggles the screen reader.
    /// </summary>
    public bool ReaderChord { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether the backend is <c>drm</c>, which adds libinput and the secondary cards.
    /// </summary>
    public bool IsDrm => Backend == "drm";
}
