using Basin.Diagnostics;
using Hexa.NET.ImGui;
using Hexa.NET.ImGuizmo;
using Hexa.NET.ImNodes;
using Hexa.NET.ImPlot;
using ImGuiApi = Hexa.NET.ImGui.ImGui;

namespace Basin.UI.ImGui;

public sealed unsafe class ImGuiContextSet : IDisposable
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private ImGuiContextPtr _imgui;
    private ImPlotContextPtr _plot;
    private ImNodesContextPtr _nodes;

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

    public bool IsDisposed => _imgui.Handle is null;

    public ImGuiContextPtr Context => _imgui;

    public ImPlotContextPtr Plot => _plot;

    public ImNodesContextPtr Nodes => _nodes;

    public Scope Enter()
    {
        _thread.Assert();
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        var previous = Scope.Capture();
        MakeCurrent(_imgui, _plot, _nodes);
        return previous;
    }

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

        public void Dispose()
        {
            if (_restore)
            {
                MakeCurrent(ImGui, Plot, Nodes);
            }
        }
    }
}
