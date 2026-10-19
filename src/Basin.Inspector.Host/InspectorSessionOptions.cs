using Basin.Host;
using Basin.Ipc;
using Basin.Scene;
using Basin.UI.ImGui;

namespace Basin.Inspector;

public sealed class InspectorSessionOptions
{
    public required OutputDriver Driver { get; init; }

    public required Scene.Scene Scene { get; init; }

    public SceneTree? Layer { get; init; }

    public SceneNode? Below { get; init; }

    public required ICompositorEventLoop Loop { get; init; }

    public required Seat.Seat Seat { get; init; }

    public BasinServices? Services { get; init; }

    public IRenderer? Renderer { get; init; }

    public IAllocator? Allocator { get; init; }

    public string? RenderNode { get; init; }

    public ImGuiUIHost? Host { get; init; }

    public InspectorSettings Settings { get; init; } = new();

    public Func<OutputView?>? DefaultOutput { get; init; }

    public Func<(double X, double Y)>? PointerPosition { get; init; }

    public IpcLineReport? Report { get; init; }

    public FrameSettle? Settle { get; init; }

    public Action<InspectorOverlay>? AddPanels { get; init; }
}
