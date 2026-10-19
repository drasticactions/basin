using System.Numerics;
using Basin.Inspector;
using Hexa.NET.ImGui;
using ImGuiApi = Hexa.NET.ImGui.ImGui;

namespace DungeonWm;

internal sealed class InspectorAudioPanel : IInspectorPanel
{
    private readonly DungeonWm _comp;

    internal InspectorAudioPanel(DungeonWm comp)
    {
        _comp = comp;
    }

    public ReadOnlySpan<byte> Name => "Audio"u8;

    public void Draw(ref InspectorDraw draw)
    {
        draw.RequestRefresh(4);
        var text = draw.Text;
        var sounds = _comp.Sounds;
        ImGuiApi.TextUnformatted(text.Format($"{sounds.Count} windows with sound"));
        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY;
        if (ImGuiApi.BeginTable("voices"u8, 6, flags, new Vector2(0, 0)))
        {
            ImGuiApi.TableSetupColumn("window"u8);
            ImGuiApi.TableSetupColumn("app"u8);
            ImGuiApi.TableSetupColumn("matched by"u8);
            ImGuiApi.TableSetupColumn("streams"u8);
            ImGuiApi.TableSetupColumn("voice"u8);
            ImGuiApi.TableSetupColumn("room"u8);
            ImGuiApi.TableHeadersRow();
            foreach (var (_, sound) in sounds)
            {
                ImGuiApi.TableNextRow();
                ImGuiApi.TableNextColumn();
                ImGuiApi.TextUnformatted(text.Format($"{sound.Id}"));
                ImGuiApi.TableNextColumn();
                ImGuiApi.TextUnformatted(text.Encode(sound.App));
                ImGuiApi.TableNextColumn();
                ImGuiApi.TextUnformatted(text.Encode(sound.Via));
                ImGuiApi.TableNextColumn();
                ImGuiApi.TextUnformatted(text.Format($"{sound.Streams.Count}"));
                ImGuiApi.TableNextColumn();
                ImGuiApi.TextUnformatted(sound.Voice is { } voice ? text.Encode(voice.Name) : "none"u8);
                ImGuiApi.TableNextColumn();
                ImGuiApi.TextUnformatted(text.Format($"{(sound.Region is { } region ? region.Id : -1)}"));
            }

            ImGuiApi.EndTable();
        }
    }
}
