namespace Basin.WindowManager;

public static class WmOutputPolicy
{
    public static Rect UsableArea(WmOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var area = output.NonExclusiveArea;
        return area.IsEmpty ? output.Area : area;
    }
}
