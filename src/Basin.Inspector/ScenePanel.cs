using System.Numerics;
using Basin.Host;
using Basin.Scene;
using Hexa.NET.ImGui;
using Hexa.NET.ImNodes;
using ImGuiApi = Hexa.NET.ImGui.ImGui;

namespace Basin.Inspector;

public sealed unsafe class ScenePanel : IInspectorPanel, IDisposable
{
    public const int MaxGraphNodes = 128;

    private readonly SceneTree _root;
    private readonly OutputDriver? _driver;
    private readonly SceneNode?[] _nodes = new SceneNode?[MaxGraphNodes];
    private readonly int[] _parents = new int[MaxGraphNodes];
    private readonly int[] _depths = new int[MaxGraphNodes];
    private readonly int[] _rows = new int[MaxGraphNodes];
    private int _count;
    private int _placedCount = -1;
    private bool _graph;

    public ScenePanel(SceneTree root, OutputDriver? driver = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        _root = root;
        _driver = driver;
    }

    public ReadOnlySpan<byte> Name => "Scene"u8;

    public int RefreshHz { get; set; } = 4;

    public void Draw(ref InspectorDraw draw)
    {
        draw.RequestRefresh(RefreshHz);
        var text = draw.Text;
        if (_driver is { } driver)
        {
            var tint = driver.DebugDamageTint;
            if (ImGuiApi.Checkbox("damage tint"u8, ref tint))
            {
                driver.DebugDamageTint = tint;
            }

            ImGuiApi.SameLine();
        }

        ImGuiApi.Checkbox("graph"u8, ref _graph);
        Collect();
        ImGuiApi.SameLine();
        ImGuiApi.TextUnformatted(text.Format($"{_count} nodes{(_count == MaxGraphNodes ? " (capped)" : string.Empty)}"));

        if (_graph)
        {
            Graph(text);
            return;
        }

        if (ImGuiApi.BeginChild("tree"u8, new Vector2(0, 0), ImGuiChildFlags.Borders))
        {
            Tree(text, 0);
        }

        ImGuiApi.EndChild();
    }

    public void Dispose()
    {
        Array.Clear(_nodes);
        _count = 0;
    }

    private void Collect()
    {
        Array.Clear(_nodes, 0, _count);
        _count = 0;
        Walk(_root, -1, 0);
    }

    private void Walk(SceneNode node, int parent, int depth)
    {
        if (_count == MaxGraphNodes)
        {
            return;
        }

        var index = _count++;
        _nodes[index] = node;
        _parents[index] = parent;
        _depths[index] = depth;
        if (node is SceneTree tree)
        {
            var children = tree.Children;
            for (var i = 0; i < children.Count; i++)
            {
                Walk(children[i], index, depth + 1);
            }
        }
    }

    private int Tree(InspectorText text, int index)
    {
        var node = _nodes[index]!;
        var depth = _depths[index];
        var next = index + 1;
        var hasChildren = next < _count && _depths[next] > depth;
        ImGuiApi.PushID(index);
        var flags = hasChildren ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.Leaf;
        var open = ImGuiApi.TreeNodeEx(Describe(text, node), flags);
        if (open)
        {
            while (next < _count && _depths[next] > depth)
            {
                next = Tree(text, next);
            }

            ImGuiApi.TreePop();
        }
        else
        {
            while (next < _count && _depths[next] > depth)
            {
                next++;
            }
        }

        ImGuiApi.PopID();
        return next;
    }

    private void Graph(InspectorText text)
    {
        ImNodes.BeginNodeEditor();
        var place = _placedCount != _count;
        if (place)
        {
            var row = 0;
            for (var i = 0; i < _count; i++)
            {
                _rows[i] = i == 0 || _depths[i] <= _depths[i - 1] ? row++ : row - 1;
                ImNodes.SetNodeGridSpacePos(i, new Vector2(_depths[i] * 200, _rows[i] * 72));
            }

            _placedCount = _count;
        }

        for (var i = 0; i < _count; i++)
        {
            var node = _nodes[i]!;
            ImNodes.BeginNode(i);
            ImNodes.BeginNodeTitleBar();
            ImGuiApi.TextUnformatted(Kind(node));
            ImNodes.EndNodeTitleBar();
            ImNodes.BeginInputAttribute((i * 2) + 1);
            ImGuiApi.TextUnformatted(text.Format($"{node.X},{node.Y}"));
            ImNodes.EndInputAttribute();
            ImNodes.BeginOutputAttribute((i * 2) + 2);
            ImGuiApi.TextUnformatted(node.Enabled ? "on"u8 : "off"u8);
            ImNodes.EndOutputAttribute();
            ImNodes.EndNode();
        }

        for (var i = 1; i < _count; i++)
        {
            ImNodes.Link(i, (_parents[i] * 2) + 2, (i * 2) + 1);
        }

        ImNodes.EndNodeEditor();
    }

    private static ReadOnlySpan<byte> Kind(SceneNode node) => node switch
    {
        SceneTree => "tree"u8,
        SceneBuffer => "buffer"u8,
        SceneRect => "rect"u8,
        SceneMesh => "mesh"u8,
        SceneShader => "shader"u8,
        SceneMirror => "mirror"u8,
        _ => "node"u8,
    };

    private static ReadOnlySpan<byte> Describe(InspectorText text, SceneNode node)
    {
        var enabled = node.Enabled ? string.Empty : "  off";
        return node switch
        {
            SceneTree tree => text.Format($"tree  {node.X},{node.Y}  {tree.Children.Count} {(tree.Children.Count == 1 ? "child" : "children")}{enabled}"),
            SceneBuffer { Buffer: { } buffer } scene => text.Format(
                $"buffer  {node.X},{node.Y}  {buffer.Width}x{buffer.Height} {buffer.Format}  shown {scene.DisplaySize.Width}x{scene.DisplaySize.Height}{(scene.IsOpaque ? "  opaque" : string.Empty)}  v{scene.ContentVersion}{enabled}"),
            SceneBuffer => text.Format($"buffer  {node.X},{node.Y}  empty{enabled}"),
            SceneRect rect => text.Format($"rect  {node.X},{node.Y}  {rect.Width}x{rect.Height}{enabled}"),
            _ => text.Format($"node  {node.X},{node.Y}{enabled}"),
        };
    }
}
