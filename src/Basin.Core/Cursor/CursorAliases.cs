namespace Basin;

public static class CursorAliases
{
    private static readonly Dictionary<string, string[]> Table = new()
    {
        ["left_ptr"] = ["default"],
        ["xterm"] = ["text"],
        ["hand2"] = ["pointer", "hand1"],
        ["hand1"] = ["grab", "openhand"],
        ["left_side"] = ["w-resize"],
        ["right_side"] = ["e-resize"],
        ["top_side"] = ["n-resize"],
        ["bottom_side"] = ["s-resize"],
        ["top_left_corner"] = ["nw-resize"],
        ["top_right_corner"] = ["ne-resize"],
        ["bottom_left_corner"] = ["sw-resize"],
        ["bottom_right_corner"] = ["se-resize"],
        ["sb_h_double_arrow"] = ["ew-resize", "col-resize"],
        ["sb_v_double_arrow"] = ["ns-resize", "row-resize"],
        ["bd_double_arrow"] = ["nwse-resize"],
        ["fd_double_arrow"] = ["nesw-resize"],
        ["fleur"] = ["all-scroll", "move"],
        ["all-resize"] = ["all-scroll", "fleur", "size_all"],
        ["watch"] = ["wait"],
        ["left_ptr_watch"] = ["progress"],
        ["grabbing"] = ["closedhand"],
        ["crossed_circle"] = ["not-allowed"],
        ["question_arrow"] = ["help"],
        ["dnd-none"] = ["no-drop"],
        ["plus"] = ["cell"],
    };

    public static IReadOnlyList<string> Of(string name) =>
        Table.TryGetValue(name, out var aliases) ? aliases : [];
}
