using Basin.Capabilities;
using Hexa.NET.ImGui;
using ImGuiApi = Hexa.NET.ImGui.ImGui;

namespace Basin.Inspector;

/// <summary>
/// The Windows panel. It lists each toplevel of an <see cref="IToplevelModel"/> and shows the surface tree of the selected one.
/// </summary>
public sealed unsafe class WindowsPanel : IInspectorPanel, IToplevelObserver, IDisposable
{
    private const int MaxDepth = 8;

    private readonly IToplevelModel _model;
    private ToplevelInfo[] _toplevels = new ToplevelInfo[32];
    private int _count;
    private ulong _selected;
    private bool _stale = true;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsPanel"/> class. It observes the model until <see cref="Dispose"/>.
    /// </summary>
    /// <param name="model">The toplevels that the panel lists.</param>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is <see langword="null"/>.</exception>
    public WindowsPanel(IToplevelModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
        model.AddObserver(this);
    }

    /// <inheritdoc/>
    public ReadOnlySpan<byte> Name => "Windows"u8;

    /// <summary>
    /// Gets the number of toplevels. It enumerates the model again when the model reported a change.
    /// </summary>
    public int Count
    {
        get
        {
            Refresh();
            return _count;
        }
    }

    /// <inheritdoc/>
    public void Draw(ref InspectorDraw draw)
    {
        Refresh();
        var text = draw.Text;
        ImGuiApi.TextUnformatted(text.Format($"{_count} windows"));
        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY;
        if (ImGuiApi.BeginTable("windows"u8, 6, flags, new System.Numerics.Vector2(0, 200)))
        {
            ImGuiApi.TableSetupColumn("id"u8);
            ImGuiApi.TableSetupColumn("app id"u8);
            ImGuiApi.TableSetupColumn("title"u8);
            ImGuiApi.TableSetupColumn("pid"u8);
            ImGuiApi.TableSetupColumn("geometry"u8);
            ImGuiApi.TableSetupColumn("state"u8);
            ImGuiApi.TableHeadersRow();
            for (var i = 0; i < _count; i++)
            {
                ref readonly var info = ref _toplevels[i];
                ImGuiApi.TableNextRow();
                ImGuiApi.TableNextColumn();
                if (ImGuiApi.Selectable(text.Format($"{info.Id}"), info.Id == _selected, ImGuiSelectableFlags.SpanAllColumns))
                {
                    _selected = info.Id;
                }

                ImGuiApi.TableNextColumn();
                ImGuiApi.TextUnformatted(text.Encode(info.AppId));
                ImGuiApi.TableNextColumn();
                ImGuiApi.TextUnformatted(text.Encode(info.Title));
                ImGuiApi.TableNextColumn();
                ImGuiApi.TextUnformatted(text.Format($"{info.Pid}"));
                ImGuiApi.TableNextColumn();
                var box = info.Geometry;
                ImGuiApi.TextUnformatted(text.Format($"{box.X},{box.Y} {box.Width}x{box.Height}"));
                ImGuiApi.TableNextColumn();
                ImGuiApi.TextUnformatted(text.Format($"{info.State}"));
            }

            ImGuiApi.EndTable();
        }

        if (_selected != 0 && Find(_selected) is { } selected && selected.Surface is { } surface)
        {
            ImGuiApi.SeparatorText("surface tree"u8);
            Tree(text, surface, "toplevel", 0);
        }
    }

    /// <inheritdoc/>
    public void OnToplevelAdded(ulong toplevelId) => _stale = true;

    /// <inheritdoc/>
    public void OnToplevelChanged(ulong toplevelId) => _stale = true;

    /// <inheritdoc/>
    public void OnToplevelRemoved(ulong toplevelId)
    {
        _stale = true;
        if (toplevelId == _selected)
        {
            _selected = 0;
        }
    }

    /// <summary>
    /// Stops observing the model and drops the listed toplevels. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _model.RemoveObserver(this);
        Array.Clear(_toplevels);
        _count = 0;
    }

    private void Refresh()
    {
        if (!_stale)
        {
            return;
        }

        _stale = false;
        while (true)
        {
            var count = _model.Enumerate(_toplevels);
            if (count >= 0)
            {
                Array.Clear(_toplevels, count, _count > count ? _count - count : 0);
                _count = count;
                return;
            }

            _toplevels = new ToplevelInfo[_toplevels.Length * 2];
        }
    }

    private ToplevelInfo? Find(ulong id)
    {
        for (var i = 0; i < _count; i++)
        {
            if (_toplevels[i].Id == id)
            {
                return _toplevels[i];
            }
        }

        return null;
    }

    private static void Tree(InspectorText text, Surface surface, string kind, int depth)
    {
        var buffer = surface.Current.Buffer;
        var label = buffer is null
            ? text.Format($"{kind}  role {surface.Role ?? "none"}  no buffer")
            : text.Format($"{kind}  role {surface.Role ?? "none"}  {buffer.Width}x{buffer.Height}  scale {surface.ServerScale:0.##}");
        var leaf = surface.SubsurfacesBelow.Count == 0 && surface.SubsurfacesAbove.Count == 0 || depth >= MaxDepth;
        ImGuiApi.PushID(surface.GetHashCode());
        var open = ImGuiApi.TreeNodeEx(label, leaf ? ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.DefaultOpen);
        if (open)
        {
            if (!leaf)
            {
                foreach (var below in surface.SubsurfacesBelow)
                {
                    Tree(text, below.Surface, "below", depth + 1);
                }

                foreach (var above in surface.SubsurfacesAbove)
                {
                    Tree(text, above.Surface, "above", depth + 1);
                }
            }

            ImGuiApi.TreePop();
        }

        ImGuiApi.PopID();
    }
}
