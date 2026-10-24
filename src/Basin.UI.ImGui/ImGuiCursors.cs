using Hexa.NET.ImGui;

namespace Basin.UI.ImGui;

/// <summary>
/// Maps the ImGui mouse cursors onto xcursor names.
/// </summary>
public static class ImGuiCursors
{
    /// <summary>
    /// Finds the xcursor name for an ImGui cursor.
    /// </summary>
    /// <param name="cursor">The cursor that ImGui asks for.</param>
    /// <returns><see langword="null"/> for <see cref="ImGuiMouseCursor.None"/>, which hides the cursor. "default" for a cursor that has no match.</returns>
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
