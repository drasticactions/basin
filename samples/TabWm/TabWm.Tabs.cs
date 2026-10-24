using Basin;
using Basin.Diagnostics;

namespace TabWm;

internal sealed partial class TabWm
{
    private ulong _nextTabId;

    internal TabSet? FocusedSet
    {
        get
        {
            if (_focused?.Tab?.Set is { } set && _sets.Contains(set))
            {
                return set;
            }

            if (_seat is { } seat && SetAt(seat.PointerX, seat.PointerY) is { } pointed)
            {
                return pointed;
            }

            return PrimarySet;
        }
    }

    private TabSet? PrimaryOrFocusedSet() => FocusedSet ?? PrimarySet;

    internal Tab? FindTab(ulong id)
    {
        foreach (var set in _sets)
        {
            foreach (var tab in set.Tabs)
            {
                if (tab.Id == id)
                {
                    return tab;
                }
            }
        }

        return null;
    }

    internal Tab NewTab(TabSet set, bool select, int index = -1, string? label = null)
    {
        var tab = new Tab(++_nextTabId, set, _layers.Tabs, _layers.Fullscreen) { UserLabel = label };
        index = index < 0 ? set.Tabs.Count : Math.Clamp(index, 0, set.Tabs.Count);
        set.Tabs.Insert(index, tab);
        set.Strip.Insert(index, tab.Model);
        if (set.ActiveIndex >= index && set.Tabs.Count > 1)
        {
            set.ActiveIndex++;
        }

        RefreshTab(tab);
        if (select || set.Tabs.Count == 1)
        {
            SelectTab(tab);
        }
        else
        {
            set.Strip.Select(set.ActiveIndex);
        }

        _workspaces.Changed();
        return tab;
    }

    internal bool SelectTab(Tab tab)
    {
        var set = tab.Set;
        var index = set.IndexOf(tab);
        if (index < 0)
        {
            return false;
        }

        set.ActiveIndex = index;
        foreach (var other in set.Tabs)
        {
            other.Show(ReferenceEquals(other, tab));
        }

        set.Strip.Select(index);
        if (tab.Attention)
        {
            tab.Attention = false;
            RefreshTab(tab);
        }

        UpdateStripVisibility(set);
        if (tab.Focused is { } window)
        {
            Focus(window);
        }
        else if (_focused is { } focused && ReferenceEquals(focused.Tab?.Set, set))
        {
            Unfocus();
        }

        UpdatePage(set);
        _workspaces.Changed();
        _outputs.ScheduleAll();
        return true;
    }

    internal void SelectRelative(int step)
    {
        if (FocusedSet is not { Tabs.Count: > 0 } set)
        {
            return;
        }

        var count = set.Tabs.Count;
        SelectTab(set.Tabs[(((set.ActiveIndex + step) % count) + count) % count]);
    }

    internal void RemoveTab(Tab tab)
    {
        var set = tab.Set;
        var index = set.IndexOf(tab);
        if (index < 0)
        {
            return;
        }

        foreach (var window in tab.Windows.ToArray())
        {
            window.Tab = null;
            window.Tree.Reparent(_layers.Hidden);
        }

        tab.Windows.Clear();
        tab.Closer = null;
        Detach(tab);
        tab.Destroy();
        HideBanner(set);
        _workspaces.Changed();
    }

    private void Detach(Tab tab)
    {
        var set = tab.Set;
        var index = set.IndexOf(tab);
        var wasActive = index == set.ActiveIndex;
        set.Tabs.RemoveAt(index);
        set.Strip.Remove(tab.Model);
        if (set.Tabs.Count == 0)
        {
            set.ActiveIndex = 0;
            if (_sets.Contains(set) && !_stopping)
            {
                NewTab(set, select: true);
            }

            return;
        }

        if (wasActive)
        {
            set.ActiveIndex = Math.Min(index, set.Tabs.Count - 1);
        }
        else if (index < set.ActiveIndex)
        {
            set.ActiveIndex--;
        }

        SelectTab(set.Tabs[set.ActiveIndex]);
    }

    internal void MoveTab(Tab tab, TabSet target, int index, bool select)
    {
        var source = tab.Set;
        if (ReferenceEquals(source, target))
        {
            ReorderTab(tab, index);
            if (select)
            {
                SelectTab(tab);
            }

            return;
        }

        var from = source.Work;
        tab.Show(false);
        Detach(tab);
        tab.Set = target;
        index = Math.Clamp(index, 0, target.Tabs.Count);
        target.Tabs.Insert(index, tab);
        target.Strip.Insert(index, tab.Model);
        if (target.ActiveIndex >= index && target.Tabs.Count > 1)
        {
            target.ActiveIndex++;
        }

        var to = target.Work;
        foreach (var window in tab.Windows)
        {
            window.Scale = target.Output.Scale;
            if (window.Fullscreen || window.Maximized)
            {
                Rearranged(window, target);
                continue;
            }

            var box = window.Geometry;
            window.MoveTo(box.X - from.X + to.X, box.Y - from.Y + to.Y);
            Clamp(window);
        }

        if (select)
        {
            SelectTab(tab);
        }
        else
        {
            target.Strip.Select(target.ActiveIndex);
        }

        BasinReport.Line($"TABMOVED tab={tab.Id} output={target.Output.Name} index={target.IndexOf(tab)}");
        _workspaces.Changed();
    }

    internal void ReorderTab(Tab tab, int index)
    {
        var set = tab.Set;
        var from = set.IndexOf(tab);
        index = Math.Clamp(index, 0, set.Tabs.Count - 1);
        if (from < 0 || from == index)
        {
            return;
        }

        var active = set.Active;
        set.Tabs.RemoveAt(from);
        set.Tabs.Insert(index, tab);
        set.Strip.Move(tab.Model, index);
        set.ActiveIndex = active is null ? 0 : set.IndexOf(active);
        _workspaces.Changed();
    }

    internal void RenameTab(Tab tab, string? label)
    {
        tab.UserLabel = label;
        RefreshTab(tab);
    }

    internal void RefreshTab(Tab tab)
    {
        var model = tab.Model;
        var label = tab.Label;
        if (model.Label != label)
        {
            model.Label = label;
            _workspaces.Changed();
        }

        if (tab.Labeled is { } window)
        {
            var (name, image) = IconFor(window.AppId);
            model.IconName = name;
            model.Icon = image;
        }
        else
        {
            model.IconName = "tab-new";
            model.Icon = null;
        }

        model.Audio = _audio is null ? TabAudioState.None : tab.Audio;
        model.Attention = tab.Attention;
        model.Busy = tab.Closer is not null;
        model.Hidden.Clear();
        foreach (var hidden in tab.Windows)
        {
            if (hidden.Mapped && hidden.Minimized)
            {
                model.Hidden.Add(new Chrome.HiddenWindowModel(hidden.Id, hidden.Title.Length > 0 ? hidden.Title : hidden.AppId));
            }
        }

        model.RaiseHidden();
        tab.Set.Strip.AnyAudio = _sets.Any(set => set.Tabs.Any(t => t.Playing && !t.Muted));
    }

    internal string Describe(Tab tab)
    {
        var set = tab.Set;
        var outputIndex = _sets.IndexOf(set);
        var windows = tab.Windows.Count(w => w.Mapped);
        var audio = tab.Audio switch
        {
            TabAudioState.Playing => "playing",
            TabAudioState.Muted => "muted",
            _ => "none",
        };
        return $"TAB output={outputIndex} index={set.IndexOf(tab)} id={tab.Id} label=\"{tab.Label}\" windows={windows} " +
            $"active={(ReferenceEquals(set.Active, tab) ? 1 : 0)} audio={audio} attention={(tab.Attention ? 1 : 0)} " +
            $"closing={(tab.Closer is null ? "no" : tab.Closer.State.ToString().ToLowerInvariant())}";
    }
}
