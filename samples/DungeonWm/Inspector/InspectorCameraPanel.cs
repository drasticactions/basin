using Basin.Inspector;
using ImGuiApi = Hexa.NET.ImGui.ImGui;

namespace DungeonWm;

internal sealed class InspectorCameraPanel : IInspectorPanel
{
    private readonly DungeonWm _comp;

    internal InspectorCameraPanel(DungeonWm comp)
    {
        _comp = comp;
    }

    public ReadOnlySpan<byte> Name => "Camera"u8;

    public void Draw(ref InspectorDraw draw)
    {
        draw.RequestRefresh(10);
        var text = draw.Text;
        var camera = _comp.WorldCamera;
        var at = camera.Position;
        ImGuiApi.TextUnformatted(text.Format($"position {at.X:0.00},{at.Y:0.00},{at.Z:0.00}"));
        ImGuiApi.TextUnformatted(text.Format(
            $"yaw {camera.Yaw * 180 / MathF.PI:0.0}  pitch {camera.Pitch * 180 / MathF.PI:0.0}  fov {camera.FieldOfView * 180 / MathF.PI:0.0}"));
        ImGuiApi.TextUnformatted(text.Format($"near {camera.Near:0.00}  far {camera.Far:0.0}"));
        if (_comp.ActiveRoom is { } room)
        {
            ImGuiApi.TextUnformatted(text.Format($"room {room.Id} ({room.Kind})  {room.Width}x{room.Depth} at {room.X0},{room.Z0}"));
        }
        else
        {
            ImGuiApi.TextUnformatted("room none"u8);
        }
    }
}
