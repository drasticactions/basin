namespace TabWm;

internal static class TabPlacement
{
    public static (T Tab, PlacementVia Via) Choose<T>(T? parent, T? token, T? pid, T active)
        where T : class
    {
        if (parent is not null)
        {
            return (parent, PlacementVia.Parent);
        }

        if (token is not null)
        {
            return (token, PlacementVia.Token);
        }

        if (pid is not null)
        {
            return (pid, PlacementVia.Pid);
        }

        return (active, PlacementVia.Active);
    }

    public static string Name(PlacementVia via) => via switch
    {
        PlacementVia.Parent => "parent",
        PlacementVia.Token => "token",
        PlacementVia.Pid => "pid",
        _ => "active",
    };
}
