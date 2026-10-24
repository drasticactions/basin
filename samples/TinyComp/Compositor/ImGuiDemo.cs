using System.Numerics;
using Basin;
using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Scene;
using Basin.UI.ImGui;
using Hexa.NET.ImGui;
using Hexa.NET.ImNodes;
using Hexa.NET.ImPlot;
using ImGuiApi = Hexa.NET.ImGui.ImGui;

namespace TinyComp;

internal sealed unsafe class ImGuiDemo : IImGuiContent, IDisposable
{
    private const int MaxLinks = 32;

    private readonly UISurfaceNode _node;
    private readonly BasinLogger _log;
    private readonly int[] _linkStart = new int[MaxLinks];
    private readonly int[] _linkEnd = new int[MaxLinks];
    private int _links;
    private int _frames;
    private bool _checked;
    private bool _disposed;

    internal ImGuiDemo(SceneTree parent, ImGuiUIHost host, UISurfaceIndex index, BasinLogger log)
    {
        _log = log;
        _node = new UISurfaceNode(parent, host, index)
        {
            Target = UITargetKind.Dmabuf,
            InputEnabled = true,
            InputFromSurface = true,
            PreciseDamage = true,
        };
        _node.Faulted += error => log.Error(error, $"imgui demo: surface faulted");
        _linkStart[0] = 2;
        _linkEnd[0] = 103;
        _links = 1;
        log.Info($"imgui demo: Dear ImGui {ImGuiNative.Version} on the {host.Name} host");
    }

    internal event Action<ImGuiUISurface>? SurfaceCreated;

    internal ImGuiUISurface? Surface => _node.Surface as ImGuiUISurface;

    internal bool Place(in Box output, double scale)
    {
        if (_disposed || output.IsEmpty)
        {
            return false;
        }

        var had = Surface;
        _node.SetPosition(output.X, output.Y);
        if (!_node.Configure(output.Width, output.Height, scale) || Surface is not { } surface)
        {
            return false;
        }

        if (!ReferenceEquals(had, surface))
        {
            surface.Content = this;
            surface.Faulted += error => _log.Error(error, $"imgui demo: the content threw");
            SurfaceCreated?.Invoke(surface);
        }

        return true;
    }

    public void Build(ImGuiFrame frame)
    {
        _frames++;
        var place = _frames == 2 ? ImGuiCond.Always : ImGuiCond.FirstUseEver;

        ImGuiApi.SetNextWindowPos(new Vector2(40, 40), ImGuiCond.FirstUseEver);
        ImGuiApi.SetNextWindowSize(new Vector2(280, 110), ImGuiCond.FirstUseEver);
        if (ImGuiApi.Begin("basin"u8))
        {
            ImGuiApi.Checkbox("checked"u8, ref _checked);
            ImGuiApi.TextUnformatted(_checked ? "the box is checked"u8 : "the box is clear"u8);
        }

        ImGuiApi.End();

        ImGuiApi.ShowDemoWindow();
        ImPlot.ShowDemoWindow();
        ShowNodes();
        if (place == ImGuiCond.Always)
        {
            ImGuiApi.SetWindowPos("Dear ImGui Demo"u8, new Vector2(40, 170), place);
            ImGuiApi.SetWindowSize("Dear ImGui Demo"u8, new Vector2(420, 500), place);
            ImGuiApi.SetWindowPos("ImPlot Demo"u8, new Vector2(480, 40), place);
            ImGuiApi.SetWindowSize("ImPlot Demo"u8, new Vector2(560, 400), place);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _node.Dispose();
    }

    private void ShowNodes()
    {
        ImGuiApi.SetNextWindowPos(new Vector2(480, 460), ImGuiCond.FirstUseEver);
        ImGuiApi.SetNextWindowSize(new Vector2(560, 240), ImGuiCond.FirstUseEver);
        if (ImGuiApi.Begin("ImNodes"u8))
        {
            ImNodes.BeginNodeEditor();
            if (_frames == 1)
            {
                ImNodes.SetNodeGridSpacePos(1, new Vector2(20, 20));
                ImNodes.SetNodeGridSpacePos(3, new Vector2(200, 60));
                ImNodes.SetNodeGridSpacePos(5, new Vector2(380, 20));
            }

            Node(1, "source"u8, output: 2);
            Node(3, "effect"u8, output: 4);
            Node(5, "post"u8, output: 6);
            for (var i = 0; i < _links; i++)
            {
                ImNodes.Link(1000 + i, _linkStart[i], _linkEnd[i]);
            }

            ImNodes.EndNodeEditor();
            int start;
            int end;
            if (ImNodes.IsLinkCreated(&start, &end) && _links < MaxLinks)
            {
                _linkStart[_links] = start;
                _linkEnd[_links] = end;
                _links++;
            }

            int destroyed;
            if (ImNodes.IsLinkDestroyed(&destroyed) && destroyed >= 1000 && destroyed - 1000 < _links)
            {
                var at = destroyed - 1000;
                _links--;
                _linkStart[at] = _linkStart[_links];
                _linkEnd[at] = _linkEnd[_links];
            }
        }

        ImGuiApi.End();
    }

    private static void Node(int id, ReadOnlySpan<byte> title, int output)
    {
        ImNodes.BeginNode(id);
        ImNodes.BeginNodeTitleBar();
        ImGuiApi.TextUnformatted(title);
        ImNodes.EndNodeTitleBar();
        ImNodes.BeginInputAttribute(id + 100);
        ImGuiApi.TextUnformatted("in"u8);
        ImNodes.EndInputAttribute();
        ImNodes.BeginOutputAttribute(output);
        ImGuiApi.TextUnformatted("out"u8);
        ImNodes.EndOutputAttribute();
        ImNodes.EndNode();
    }
}
