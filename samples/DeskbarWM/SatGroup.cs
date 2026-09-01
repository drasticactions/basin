namespace DeskbarWm;

internal sealed class SatGroup
{
    public List<SatArea> Areas { get; } = [];

    public IReadOnlyList<SatTab> Tabs
    {
        get
        {
            var tabs = new List<SatTab>();
            foreach (var area in Areas)
            {
                AddDistinct(tabs, area.Left);
                AddDistinct(tabs, area.Top);
                AddDistinct(tabs, area.Right);
                AddDistinct(tabs, area.Bottom);
            }

            return tabs;
        }
    }

    public int References(SatTab tab)
    {
        var count = 0;
        foreach (var area in Areas)
        {
            if (area.Touches(tab))
            {
                count++;
            }
        }

        return count;
    }

    private static void AddDistinct(List<SatTab> tabs, SatTab tab)
    {
        if (!tabs.Contains(tab))
        {
            tabs.Add(tab);
        }
    }
}
