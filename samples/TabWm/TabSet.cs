using Basin;
using Basin.Scene;
using TabWm.Chrome;

namespace TabWm;

internal sealed class TabSet
{
    public TabSet(ulong id, IOutput output)
    {
        Id = id;
        Output = output;
    }

    public ulong Id { get; }

    public IOutput Output { get; }

    public List<Tab> Tabs { get; } = [];

    public int ActiveIndex { get; set; }

    public Tab? Active => ActiveIndex >= 0 && ActiveIndex < Tabs.Count ? Tabs[ActiveIndex] : null;

    public StripModel Strip { get; } = new();

    public OutputUISurface? StripSurface { get; set; }

    public StripView? StripView { get; set; }

    public OutputUISurface? Page { get; set; }

    public OutputUISurface? Banner { get; set; }

    public SceneRect? Background { get; set; }

    public int StripHeight { get; set; } = 40;

    public bool StripHidden { get; set; }

    public bool StripPeek { get; set; }

    public Box Box { get; set; }

    public Box Usable { get; set; }

    public Box StripBox => new(Usable.X, Usable.Y, Usable.Width, StripHeight);

    public Box Work => new(Usable.X, Usable.Y + StripHeight, Usable.Width, Math.Max(1, Usable.Height - StripHeight));

    public int IndexOf(Tab tab) => Tabs.IndexOf(tab);
}
