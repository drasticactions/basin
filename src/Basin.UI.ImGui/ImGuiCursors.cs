using Hexa.NET.ImGui;

namespace Basin.UI.ImGui;

public static class ImGuiCursors
{
    public static string? NameOf(ImGuiMouseCursor cursor) => cursor switch
    {
        ImGuiMouseCursor.None => null,
        ImGuiMouseCursor.Arrow => "default",
        ImGuiMouseCursor.TextInput => "text",
        ImGuiMouseCursor.ResizeAll => "move",
        ImGuiMouseCursor.ResizeNs => "ns-resize",
        ImGuiMouseCursor.ResizeEw => "ew-resize",
        ImGuiMouseCursor.ResizeNesw => "nesw-resize",
        ImGuiMouseCursor.ResizeNwse => "nwse-resize",
        ImGuiMouseCursor.Hand => "pointer",
        ImGuiMouseCursor.Wait => "wait",
        ImGuiMouseCursor.Progress => "progress",
        ImGuiMouseCursor.NotAllowed => "not-allowed",
        _ => "default",
    };
}
