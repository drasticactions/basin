using System.Numerics;
using Basin.Inspector;
using Hexa.NET.ImGui;
using ImGuiApi = Hexa.NET.ImGui.ImGui;

namespace DungeonWm;

internal sealed class InspectorPhysicsPanel : IInspectorPanel
{
    private readonly DungeonWm _comp;

    internal InspectorPhysicsPanel(DungeonWm comp)
    {
        _comp = comp;
    }

    public ReadOnlySpan<byte> Name => "Physics"u8;

    public int RefreshHz { get; set; } = 10;

    public void Draw(ref InspectorDraw draw)
    {
        draw.RequestRefresh(RefreshHz);
        var text = draw.Text;
        if (_comp.PhysicsWorld is not { } physics)
        {
            ImGuiApi.TextUnformatted("physics is off"u8);
            return;
        }

        var freeze = _comp.InspectorFreeze;
        if (ImGuiApi.Checkbox("freeze"u8, ref freeze))
        {
            _comp.InspectorFreeze = freeze;
            _comp.Report($"PHYSICS frozen={(freeze ? "yes" : "no")}");
            _comp.Repaint();
        }

        var (moved, impacts, total) = _comp.StepCounts;
        var world = physics.World;
        ImGuiApi.TextUnformatted(text.Format(
            $"awake {(world.IsAwake ? "yes" : "no")}  frozen {(world.Frozen ? "yes" : "no")}  bodies {world.Count}  windows {physics.Bodies.Count}  props {physics.PropBodies}"));
        ImGuiApi.TextUnformatted(text.Format($"last step: moved {moved}  impacts {impacts}  impacts in all {total}"));
        var player = physics.Player.Position;
        ImGuiApi.TextUnformatted(text.Format(
            $"player {player.X:0.00},{player.Y:0.00},{player.Z:0.00}  supported {(physics.Player.Supported ? "yes" : "no")}"));

        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY;
        if (ImGuiApi.BeginTable("bodies"u8, 4, flags, new Vector2(0, 0)))
        {
            ImGuiApi.TableSetupColumn("window"u8);
            ImGuiApi.TableSetupColumn("at"u8);
            ImGuiApi.TableSetupColumn("speed"u8, ImGuiTableColumnFlags.WidthFixed, 70);
            ImGuiApi.TableSetupColumn("awake"u8, ImGuiTableColumnFlags.WidthFixed, 60);
            ImGuiApi.TableHeadersRow();
            foreach (var (window, body) in physics.Bodies)
            {
                var pose = body.Pose;
                ImGuiApi.TableNextRow();
                ImGuiApi.TableNextColumn();
                ImGuiApi.TextUnformatted(text.Encode(window.AppId));
                ImGuiApi.TableNextColumn();
                ImGuiApi.TextUnformatted(text.Format($"{pose.Position.X:0.00},{pose.Position.Y:0.00},{pose.Position.Z:0.00}"));
                ImGuiApi.TableNextColumn();
                ImGuiApi.TextUnformatted(text.Format($"{body.Velocity.Length():0.00}"));
                ImGuiApi.TableNextColumn();
                ImGuiApi.TextUnformatted(body.IsAwake ? "yes"u8 : "no"u8);
            }

            ImGuiApi.EndTable();
        }
    }
}
