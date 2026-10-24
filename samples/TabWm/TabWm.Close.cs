using Basin;
using Basin.Diagnostics;
using Basin.Scene;
using Basin.UI.Avalonia;
using TabWm.Chrome;

namespace TabWm;

internal sealed partial class TabWm
{
    private const int BannerHeight = 44;

    private IEventSource? _closeTimer;

    internal void CloseTab(Tab tab)
    {
        if (tab.Closer is { State: TabCloseState.Refused or TabCloseState.Waiting })
        {
            return;
        }

        var windows = tab.Windows.Where(window => window.Mapped).ToList();
        BasinReport.Line($"CLOSING tab={tab.Id} windows={windows.Count}");
        if (windows.Count == 0)
        {
            RemoveTab(tab);
            BasinReport.Line($"CLOSED tab={tab.Id}");
            return;
        }

        tab.Closer = new TabCloser<TabWindow>(windows, Environment.TickCount64, _config.CloseTimeoutMs, window => window.Close());
        RefreshTab(tab);
        ArmCloseTimer();
    }

    private void ArmCloseTimer()
    {
        long? next = null;
        foreach (var set in _sets)
        {
            foreach (var tab in set.Tabs)
            {
                if (tab.Closer is { State: TabCloseState.Waiting } closer && (next is null || closer.Deadline < next))
                {
                    next = closer.Deadline;
                }
            }
        }

        if (next is not { } deadline)
        {
            _closeTimer?.UpdateTimer(0);
            return;
        }

        _closeTimer ??= _host.Loop.AddTimer(OnCloseTimer);
        _closeTimer.UpdateTimer((int)Math.Max(1, deadline - Environment.TickCount64));
    }

    private void OnCloseTimer()
    {
        var now = Environment.TickCount64;
        foreach (var set in _sets.ToArray())
        {
            foreach (var tab in set.Tabs.ToArray())
            {
                if (tab.Closer is { State: TabCloseState.Waiting } closer && closer.Tick(now) == TabCloseState.Refused)
                {
                    Refused(tab);
                }
            }
        }

        ArmCloseTimer();
    }

    private void Refused(Tab tab)
    {
        BasinReport.Line($"BANNER tab={tab.Id} windows={tab.Windows.Count(window => window.Mapped)}");
        SelectTab(tab);
        ShowBanner(tab);
        _access?.Tree?.Announce($"{tab.Label} did not close. Its windows are still open.", Basin.Capabilities.AnnouncePriority.Assertive);
        RefreshTab(tab);
    }

    internal void KeepTab(Tab tab)
    {
        if (tab.Closer is null)
        {
            return;
        }

        tab.Closer.Cancel();
        EndClose(tab, removed: false);
    }

    private void EndClose(Tab tab, bool removed)
    {
        tab.Closer = null;
        HideBanner(tab.Set);
        if (removed)
        {
            RemoveTab(tab);
            BasinReport.Line($"CLOSED tab={tab.Id}");
        }
        else
        {
            BasinReport.Line($"KEPT tab={tab.Id}");
            RefreshTab(tab);
            UpdatePage(tab.Set);
        }

        ArmCloseTimer();
        _outputs.ScheduleAll();
    }

    private static Box BannerBox(TabSet set)
    {
        var work = set.Work;
        return new Box(work.X, work.Y, work.Width, BannerHeight);
    }

    private void ShowBanner(Tab tab)
    {
        var set = tab.Set;
        HideBanner(set);
        var model = new BannerModel(
            id =>
            {
                if (FindWindow(id) is { } window)
                {
                    Activate(window);
                }
            },
            () => KeepTab(tab))
        {
            Message = "These windows did not close:",
        };
        foreach (var window in tab.Windows)
        {
            if (window.Mapped)
            {
                model.Windows.Add(new HiddenWindowModel(window.Id, window.Title.Length > 0 ? window.Title : window.AppId));
            }
        }

        var banner = new OutputUISurface(_layers.Strip, _ui, _index) { PreciseDamage = true, AccessibleName = "Closing tab" };
        banner.Realized += surface => ((AvaloniaUISurface)surface).Content = new BannerView { DataContext = model };
        set.Banner = banner;
        banner.PlaceAt(BannerBox(set), set.Output.Scale);
        _outputs.ScheduleAll();
    }

    private void HideBanner(TabSet set)
    {
        set.Banner?.Dispose();
        set.Banner = null;
    }
}
