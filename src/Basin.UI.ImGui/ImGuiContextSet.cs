using Basin.Diagnostics;
using Hexa.NET.ImGui;
using Hexa.NET.ImGuizmo;
using Hexa.NET.ImNodes;
using Hexa.NET.ImPlot;
using ImGuiApi = Hexa.NET.ImGui.ImGui;

namespace Basin.UI.ImGui;

/// <summary>
/// One ImGui context with its ImPlot and ImNodes contexts, and ImGuizmo bound to it. Each native library holds its own current context, so every entry goes through <see cref="Enter"/>.
/// </summary>
public sealed unsafe class ImGuiContextSet : IDisposable
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private ImGuiContextPtr _imgui;
    private ImPlotContextPtr _plot;
    private ImNodesContextPtr _nodes;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImGuiContextSet"/> class. The contexts write no ini or log file, and recover from a missing End without an assert.
    /// </summary>
    /// <param name="sharedAtlas">The font atlas that the context shares and does not own. A null handle makes the context create its own atlas.</param>
    /// <exception cref="InvalidOperationException">The native libraries do not load or do not match the bindings.</exception>
    public ImGuiContextSet(ImFontAtlasPtr sharedAtlas)
    {
        if (!ImGuiNative.TryLoad(out var declined))
        {
            throw new InvalidOperationException(declined);
        }

        var previous = Scope.Capture();
        try
        {
            _imgui = sharedAtlas.Handle is null ? ImGuiApi.CreateContext() : ImGuiApi.CreateContext(sharedAtlas);
            ImGuiApi.SetCurrentContext(_imgui);
            ImPlot.SetImGuiContext(_imgui);
            ImNodes.SetImGuiContext(_imgui);
            ImGuizmo.SetImGuiContext(_imgui);
            _plot = ImPlot.CreateContext();
            _nodes = ImNodes.CreateContext();

            var io = ImGuiApi.GetIO();
            io.IniFilename = null;
            io.LogFilename = null;
            io.ConfigErrorRecovery = true;
            io.ConfigErrorRecoveryEnableAssert = false;
            io.ConfigErrorRecoveryEnableDebugLog = true;
            io.ConfigErrorRecoveryEnableTooltip = false;
            io.BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
        }
        finally
        {
            previous.Dispose();
        }
    }

    /// <summary>
    /// Gets a value indicating whether the contexts are destroyed.
    /// </summary>
    public bool IsDisposed => _imgui.Handle is null;

    /// <summary>
    /// Gets the ImGui context. It is a null handle after <see cref="Dispose"/>.
    /// </summary>
    public ImGuiContextPtr Context => _imgui;

    /// <summary>
    /// Gets the ImPlot context. It is a null handle after <see cref="Dispose"/>.
    /// </summary>
    public ImPlotContextPtr Plot => _plot;

    /// <summary>
    /// Gets the ImNodes context. It is a null handle after <see cref="Dispose"/>.
    /// </summary>
    public ImNodesContextPtr Nodes => _nodes;

    /// <summary>
    /// Makes these contexts current in all four native libraries. Call it on the thread that created the set.
    /// </summary>
    /// <returns>A scope that makes the previous contexts current again when it is disposed.</returns>
    /// <exception cref="ObjectDisposedException">The set is disposed.</exception>
    public Scope Enter()
    {
        _thread.Assert();
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        var previous = Scope.Capture();
        MakeCurrent(_imgui, _plot, _nodes);
        return previous;
    }

    /// <summary>
    /// Destroys the three contexts. When they were current, no context is current after the call. Other contexts that were current stay current.
    /// </summary>
    public void Dispose()
    {
        _thread.Assert();
        if (IsDisposed)
        {
            return;
        }

        var previous = Scope.Capture();
        MakeCurrent(_imgui, _plot, _nodes);
        ImNodes.DestroyContext(_nodes);
        ImPlot.DestroyContext(_plot);
        ImGuiApi.DestroyContext(_imgui);
        if (previous.ImGui == _imgui)
        {
            previous = default;
        }

        _nodes = default;
        _plot = default;
        _imgui = default;
        MakeCurrent(default, default, default);
        previous.Dispose();
    }

    private static void MakeCurrent(ImGuiContextPtr imgui, ImPlotContextPtr plot, ImNodesContextPtr nodes)
    {
        ImGuiApi.SetCurrentContext(imgui);
        ImPlot.SetImGuiContext(imgui);
        ImNodes.SetImGuiContext(imgui);
        ImGuizmo.SetImGuiContext(imgui);
        ImPlot.SetCurrentContext(plot);
        ImNodes.SetCurrentContext(nodes);
    }

    /// <summary>
    /// The contexts that were current before <see cref="Enter"/>. Disposing it makes them current again.
    /// </summary>
    public readonly ref struct Scope
    {
        private readonly bool _restore;

        private Scope(ImGuiContextPtr imgui, ImPlotContextPtr plot, ImNodesContextPtr nodes)
        {
            ImGui = imgui;
            Plot = plot;
            Nodes = nodes;
            _restore = true;
        }

        internal ImGuiContextPtr ImGui { get; }

        internal ImPlotContextPtr Plot { get; }

        internal ImNodesContextPtr Nodes { get; }

        internal static Scope Capture() =>
            new(ImGuiApi.GetCurrentContext(), ImPlot.GetCurrentContext(), ImNodes.GetCurrentContext());

        /// <summary>
        /// Makes the captured contexts current again. A default scope does nothing.
        /// </summary>
        public void Dispose()
        {
            if (_restore)
            {
                MakeCurrent(ImGui, Plot, Nodes);
            }
        }
    }
}
