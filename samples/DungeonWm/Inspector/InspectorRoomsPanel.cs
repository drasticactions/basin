using System.Numerics;
using Basin.Inspector;
using Hexa.NET.ImGui;
using ImGuiApi = Hexa.NET.ImGui.ImGui;

namespace DungeonWm;

internal sealed class InspectorRoomsPanel : IInspectorPanel
{
    private readonly DungeonWm _comp;

    internal InspectorRoomsPanel(DungeonWm comp)
    {
        _comp = comp;
    }

    public ReadOnlySpan<byte> Name => "Rooms"u8;

    public void Draw(ref InspectorDraw draw)
    {
        draw.RequestRefresh(4);
        var text = draw.Text;
        var world = _comp.WorldModel;
        if (draw.Overlay.Output is { } view && _comp.VisibleOn(view) is { } visible)
        {
            ImGuiApi.SeparatorText("visible from the camera"u8);
            ImGuiApi.TextUnformatted(text.Format(
                $"eye room {(visible.EyeRoom is { } eye ? eye.Id : -1)}  rooms {visible.Rooms.Count}  windows {visible.Windows.Count}  props {visible.Props.Count}  portals {visible.Portals.Count}  surfaces {visible.Surfaces.Count}"));
            for (var i = 0; i < visible.Rooms.Count; i++)
            {
                if (i > 0)
                {
                    ImGuiApi.SameLine();
                }

                ImGuiApi.TextUnformatted(text.Format($"{visible.Rooms[i].Id}"));
            }
        }

        ImGuiApi.SeparatorText("map"u8);
        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY;
        if (ImGuiApi.BeginTable("rooms"u8, 5, flags, new Vector2(0, 0)))
        {
            ImGuiApi.TableSetupColumn("id"u8);
            ImGuiApi.TableSetupColumn("kind"u8);
            ImGuiApi.TableSetupColumn("cells"u8);
            ImGuiApi.TableSetupColumn("workspace"u8);
            ImGuiApi.TableSetupColumn("doorways"u8);
            ImGuiApi.TableHeadersRow();
            var active = _comp.ActiveRoom;
            foreach (var region in world.Map.Rooms)
            {
                if (region.Removed)
                {
                    continue;
                }

                ImGuiApi.TableNextRow();
                ImGuiApi.TableNextColumn();
                ImGuiApi.TextUnformatted(text.Format($"{region.Id}{(ReferenceEquals(region, active) ? " *" : string.Empty)}"));
                ImGuiApi.TableNextColumn();
                ImGuiApi.TextUnformatted(region.Kind == RegionKind.Room ? "room"u8 : "corridor"u8);
                ImGuiApi.TableNextColumn();
                ImGuiApi.TextUnformatted(text.Format($"{region.Width}x{region.Depth} at {region.X0},{region.Z0}"));
                ImGuiApi.TableNextColumn();
                ImGuiApi.TextUnformatted(text.Format($"{region.Workspace}"));
                ImGuiApi.TableNextColumn();
                ImGuiApi.TextUnformatted(text.Format($"{world.RoomOf(region).Doorways.Count}"));
            }

            ImGuiApi.EndTable();
        }
    }
}
