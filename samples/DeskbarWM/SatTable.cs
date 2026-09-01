using Basin.WindowManager;

namespace DeskbarWm;

internal sealed class SatTable
{
    private readonly List<SatGroup> _groups = [];

    public IReadOnlyList<SatGroup> Groups => _groups;

    public void Stack(ManagedWindow moving, ManagedWindow target)
    {
        if (ReferenceEquals(moving, target) || ReferenceEquals(moving.Area, target.Area) && moving.Area is not null)
        {
            return;
        }

        Remove(moving);

        var area = target.Area;
        if (area is null)
        {
            var group = new SatGroup();
            var frame = target.FrameRect;
            var left = new SatTab(vertical: true, frame.X);
            var top = new SatTab(vertical: false, frame.Y);
            var right = new SatTab(vertical: true, frame.Right);
            var bottom = new SatTab(vertical: false, frame.Bottom);
            area = new SatArea(group, left, top, right, bottom);
            area.Windows.Add(target);
            area.Front = target;
            target.Area = area;
            group.Areas.Add(area);
            _groups.Add(group);
        }

        area.Windows.Add(moving);
        area.Front = moving;
        moving.Area = area;
    }

    public void TileLink(ManagedWindow moving, ManagedWindow target, Edges movingEdge, int tabHeight)
    {
        if (ReferenceEquals(moving, target))
        {
            return;
        }

        Remove(moving);

        var area = target.Area;
        SatGroup group;
        if (area is null)
        {
            group = new SatGroup();
            var targetFrame = target.FrameRect;
            area = new SatArea(
                group,
                new SatTab(true, targetFrame.X),
                new SatTab(false, targetFrame.Y),
                new SatTab(true, targetFrame.Right),
                new SatTab(false, targetFrame.Bottom));
            area.Windows.Add(target);
            area.Front = target;
            target.Area = area;
            group.Areas.Add(area);
            _groups.Add(group);
        }
        else
        {
            group = area.Group;
        }

        var frame = moving.FrameRect;
        SatTab left;
        SatTab top;
        SatTab right;
        SatTab bottom;
        switch (movingEdge)
        {
            case Edges.Left:
                left = area.Right;
                top = new SatTab(false, frame.Y);
                right = new SatTab(true, area.Right.Position + frame.Width);
                bottom = new SatTab(false, frame.Bottom);
                break;
            case Edges.Right:
                right = area.Left;
                top = new SatTab(false, frame.Y);
                left = new SatTab(true, area.Left.Position - frame.Width);
                bottom = new SatTab(false, frame.Bottom);
                break;
            case Edges.Top:
                top = area.Bottom;
                left = new SatTab(true, frame.X);
                right = new SatTab(true, frame.Right);
                bottom = new SatTab(false, area.Bottom.Position + frame.Height);
                break;
            default:
                bottom = area.Top;
                left = new SatTab(true, frame.X);
                right = new SatTab(true, frame.Right);
                top = new SatTab(false, area.Top.Position - frame.Height);
                break;
        }

        var joined = new SatArea(group, left, top, right, bottom);
        joined.Windows.Add(moving);
        joined.Front = moving;
        moving.Area = joined;
        group.Areas.Add(joined);
        _ = tabHeight;
    }

    public void Remove(ManagedWindow mw)
    {
        if (mw.Area is not { } area)
        {
            return;
        }

        mw.Area = null;
        area.Windows.Remove(mw);
        if (ReferenceEquals(area.Front, mw))
        {
            area.Front = area.Windows.Count > 0 ? area.Windows[^1] : null;
        }

        var group = area.Group;
        if (area.Windows.Count == 0)
        {
            Collapse(group, area);
        }

        Dissolve(group);
    }

    public void Translate(SatGroup group, int dx, int dy)
    {
        foreach (var tab in group.Tabs)
        {
            tab.Position += tab.Vertical ? dx : dy;
        }
    }

    public void MoveTab(SatGroup group, SatTab tab, int position, int minCell)
    {
        var lowest = int.MinValue;
        var highest = int.MaxValue;
        foreach (var area in group.Areas)
        {
            if (ReferenceEquals(area.Right, tab) || ReferenceEquals(area.Bottom, tab))
            {
                var opposite = ReferenceEquals(area.Right, tab) ? area.Left.Position : area.Top.Position;
                lowest = Math.Max(lowest, opposite + minCell);
            }

            if (ReferenceEquals(area.Left, tab) || ReferenceEquals(area.Top, tab))
            {
                var opposite = ReferenceEquals(area.Left, tab) ? area.Right.Position : area.Bottom.Position;
                highest = Math.Min(highest, opposite - minCell);
            }
        }

        tab.Position = Math.Clamp(position, lowest, highest);
    }

    private void Collapse(SatGroup group, SatArea removed)
    {
        group.Areas.Remove(removed);

        SatTab? chosen = null;
        var fewest = int.MaxValue;
        foreach (var tab in (SatTab[])[removed.Left, removed.Top, removed.Right, removed.Bottom])
        {
            var references = group.References(tab);
            if (references > 0 && references < fewest)
            {
                fewest = references;
                chosen = tab;
            }
        }

        if (chosen is null)
        {
            return;
        }

        if (ReferenceEquals(chosen, removed.Left))
        {
            ReplaceTab(group, chosen, removed.Right);
        }
        else if (ReferenceEquals(chosen, removed.Right))
        {
            ReplaceTab(group, chosen, removed.Left);
        }
        else if (ReferenceEquals(chosen, removed.Top))
        {
            ReplaceTab(group, chosen, removed.Bottom);
        }
        else
        {
            ReplaceTab(group, chosen, removed.Top);
        }
    }

    private static void ReplaceTab(SatGroup group, SatTab from, SatTab to)
    {
        foreach (var area in group.Areas)
        {
            if (ReferenceEquals(area.Left, from))
            {
                area.Left = to;
            }

            if (ReferenceEquals(area.Top, from))
            {
                area.Top = to;
            }

            if (ReferenceEquals(area.Right, from))
            {
                area.Right = to;
            }

            if (ReferenceEquals(area.Bottom, from))
            {
                area.Bottom = to;
            }
        }
    }

    private void Dissolve(SatGroup group)
    {
        if (group.Areas.Count == 1 && group.Areas[0].Windows.Count == 1)
        {
            group.Areas[0].Windows[0].Area = null;
            group.Areas.Clear();
        }

        if (group.Areas.Count == 0)
        {
            _groups.Remove(group);
        }
    }
}
