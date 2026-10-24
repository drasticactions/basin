using Basin.Host;
using Basin.Ipc;
using Basin.Scene;
using Basin.UI.ImGui;

namespace Basin.Inspector;

/// <summary>
/// The compositor parts that an <see cref="InspectorSession"/> wires the overlay to.
/// </summary>
public sealed class InspectorSessionOptions
{
    /// <summary>
    /// Gets the output driver. The session follows its outputs, and the Outputs and Scene panels read it.
    /// </summary>
    public required OutputDriver Driver { get; init; }

    /// <summary>
    /// Gets the scene. Its root is the tree of the Scene panel, and of the owned layer when <see cref="Layer"/> and <see cref="Below"/> are <see langword="null"/>.
    /// </summary>
    public required Scene.Scene Scene { get; init; }

    /// <summary>
    /// Gets the tree that receives the overlay node, ordered by the consumer. When it is <see langword="null"/>, the session makes and orders a layer of its own.
    /// </summary>
    public SceneTree? Layer { get; init; }

    /// <summary>
    /// Gets the node that the owned layer stays directly below. When it is <see langword="null"/>, the owned layer stays on top of the scene root. It is not used when <see cref="Layer"/> is set.
    /// </summary>
    public SceneNode? Below { get; init; }

    /// <summary>
    /// Gets the compositor loop that runs the overlay's <see cref="UIDriver"/> and its clipboard.
    /// </summary>
    public required ICompositorEventLoop Loop { get; init; }

    /// <summary>
    /// Gets the seat whose keyboard and pointer the overlay takes focus from, and whose keymap gives the modifier masks.
    /// </summary>
    public required Seat.Seat Seat { get; init; }

    /// <summary>
    /// Gets the services that give the toplevel model, the client directory and the selection store. A missing one drops its panel or the clipboard.
    /// </summary>
    public BasinServices? Services { get; init; }

    /// <summary>
    /// Gets the renderer that the session builds the ImGui host on. When it and <see cref="Host"/> are <see langword="null"/>, no overlay can be built.
    /// </summary>
    public IRenderer? Renderer { get; init; }

    /// <summary>
    /// Gets the buffer allocator for the ImGui host, or <see langword="null"/> to let the host choose.
    /// </summary>
    public IAllocator? Allocator { get; init; }

    /// <summary>
    /// Gets the path of the DRM render node for the ImGui host, or <see langword="null"/> to let the host choose.
    /// </summary>
    public string? RenderNode { get; init; }

    /// <summary>
    /// Gets an ImGui host that the consumer owns. The session uses it and does not dispose it. When it is <see langword="null"/>, the session builds and owns a host.
    /// </summary>
    public ImGuiUIHost? Host { get; init; }

    /// <summary>
    /// Gets the settings at start. The default is the built-in <see cref="InspectorSettings"/>.
    /// </summary>
    public InspectorSettings Settings { get; init; } = new();

    /// <summary>
    /// Gets the consumer's choice of output when the settings name none. The session tries it before the output under the pointer.
    /// </summary>
    public Func<OutputView?>? DefaultOutput { get; init; }

    /// <summary>
    /// Gets the pointer position in layout coordinates. The session opens on the output under it when no other choice gives an output.
    /// </summary>
    public Func<(double X, double Y)>? PointerPosition { get; init; }

    /// <summary>
    /// Gets the report that prints the <c>INSPECTOR</c> lines. <see cref="InspectorSession.Register"/> requires it. When it is <see langword="null"/>, the other lines go to <see cref="Basin.Diagnostics.BasinReport"/>.
    /// </summary>
    public IpcLineReport? Report { get; init; }

    /// <summary>
    /// Gets the consumer's frame settle at start. The session adds its <see cref="UIDriver"/> there when it builds the host.
    /// </summary>
    public FrameSettle? Settle { get; init; }

    /// <summary>
    /// Gets the callback that adds the consumer's panels. The session calls it once after the library panels, before it shows the configured panels.
    /// </summary>
    public Action<InspectorOverlay>? AddPanels { get; init; }
}
