using System.Numerics;
using Basin.Capabilities;
using Hexa.NET.ImGui;
using ImGuiApi = Hexa.NET.ImGui.ImGui;

namespace Basin.Inspector;

/// <summary>
/// The Clients panel. It lists the id, pid, uid and gid of each Wayland client, and the interfaces that the selected client holds objects of.
/// </summary>
public sealed class ClientsPanel : IInspectorPanel
{
    private readonly IClientDirectory _directory;
    private ClientInfo[] _clients = new ClientInfo[16];
    private ClientObjectInfo[] _objects = new ClientObjectInfo[64];
    private ulong _selected;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClientsPanel"/> class.
    /// </summary>
    /// <param name="directory">The source of the client list and of each client's objects.</param>
    /// <exception cref="ArgumentNullException"><paramref name="directory"/> is <see langword="null"/>.</exception>
    public ClientsPanel(IClientDirectory directory)
    {
        ArgumentNullException.ThrowIfNull(directory);
        _directory = directory;
    }

    /// <inheritdoc/>
    public ReadOnlySpan<byte> Name => "Clients"u8;

    /// <summary>
    /// Gets or sets the rate in frames per second at which the panel redraws while it is shown. The default is 2.
    /// </summary>
    public int RefreshHz { get; set; } = 2;

    /// <inheritdoc/>
    public void Draw(ref InspectorDraw draw)
    {
        draw.RequestRefresh(RefreshHz);
        var text = draw.Text;
        var count = Enumerate();
        ImGuiApi.TextUnformatted(text.Format($"{count} clients"));
        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY;
        if (ImGuiApi.BeginTable("clients"u8, 4, flags, new Vector2(0, 160)))
        {
            ImGuiApi.TableSetupColumn("id"u8);
            ImGuiApi.TableSetupColumn("pid"u8);
            ImGuiApi.TableSetupColumn("uid"u8);
            ImGuiApi.TableSetupColumn("gid"u8);
            ImGuiApi.TableHeadersRow();
            for (var i = 0; i < count; i++)
            {
                ref readonly var client = ref _clients[i];
                ImGuiApi.TableNextRow();
                ImGuiApi.TableNextColumn();
                if (ImGuiApi.Selectable(text.Format($"{client.Id}"), client.Id == _selected, ImGuiSelectableFlags.SpanAllColumns))
                {
                    _selected = client.Id;
                }

                ImGuiApi.TableNextColumn();
                ImGuiApi.TextUnformatted(text.Format($"{client.Pid}"));
                ImGuiApi.TableNextColumn();
                ImGuiApi.TextUnformatted(text.Format($"{client.Uid}"));
                ImGuiApi.TableNextColumn();
                ImGuiApi.TextUnformatted(text.Format($"{client.Gid}"));
            }

            ImGuiApi.EndTable();
        }

        if (_selected == 0)
        {
            return;
        }

        if (!_directory.CanListObjects)
        {
            ImGuiApi.TextUnformatted("this transport cannot list a client's objects"u8);
            return;
        }

        var objects = Objects(_selected);
        ImGuiApi.SeparatorText(text.Format($"client {_selected}: {objects} interfaces"));
        if (ImGuiApi.BeginTable("objects"u8, 3, flags, new Vector2(0, 0)))
        {
            ImGuiApi.TableSetupColumn("interface"u8);
            ImGuiApi.TableSetupColumn("version"u8, ImGuiTableColumnFlags.WidthFixed, 70);
            ImGuiApi.TableSetupColumn("objects"u8, ImGuiTableColumnFlags.WidthFixed, 70);
            ImGuiApi.TableHeadersRow();
            for (var i = 0; i < objects; i++)
            {
                ref readonly var item = ref _objects[i];
                ImGuiApi.TableNextRow();
                ImGuiApi.TableNextColumn();
                ImGuiApi.TextUnformatted(text.Encode(item.Interface));
                ImGuiApi.TableNextColumn();
                ImGuiApi.TextUnformatted(text.Format($"{item.Version}"));
                ImGuiApi.TableNextColumn();
                ImGuiApi.TextUnformatted(text.Format($"{item.Count}"));
            }

            ImGuiApi.EndTable();
        }
    }

    private int Enumerate()
    {
        while (true)
        {
            var count = _directory.Enumerate(_clients);
            if (count >= 0)
            {
                Array.Clear(_clients, count, _clients.Length - count);
                return count;
            }

            _clients = new ClientInfo[_clients.Length * 2];
        }
    }

    private int Objects(ulong id)
    {
        while (true)
        {
            var count = _directory.Objects(id, _objects);
            if (count >= 0)
            {
                Array.Sort(_objects, 0, count, ObjectOrder.Instance);
                return count;
            }

            _objects = new ClientObjectInfo[_objects.Length * 2];
        }
    }

    private sealed class ObjectOrder : IComparer<ClientObjectInfo>
    {
        public static readonly ObjectOrder Instance = new();

        public int Compare(ClientObjectInfo x, ClientObjectInfo y)
        {
            var name = string.CompareOrdinal(x.Interface, y.Interface);
            return name != 0 ? name : x.Version.CompareTo(y.Version);
        }
    }
}
