using System.Globalization;
using Basin;
using Basin.Scene;

using Basin.Diagnostics;

namespace EightWm;

internal sealed partial class Shell
{
    private static readonly RenderColor DimColor = new(0f, 0f, 0f, 0.55f);

    private void AttachCharms(ShellView view)
    {
        view.Charms = new CharmsBar(UIHost, view.CharmsFrame, view.CharmsClockFrame, view.CharmsPaneFrame);
        view.DimRect = new SceneRect(view.DimFrame, 1, 1, DimColor) { Enabled = false };
    }

    internal void ShowCharms(ShellView view, bool visible, bool keepPane = false)
    {
        if (view.Charms is not { } charms)
        {
            return;
        }

        if (visible)
        {
            CloseOtherChrome(view, ChromePanel.Charms);
        }

        if (charms.Visible == visible)
        {
            return;
        }

        charms.Resize(view.Box.Width, view.Box.Height, view.Scale);
        charms.Show(visible);
        ArmDim(view);

        var name = visible ? Animation.ShowPanel : Animation.HidePanel;
        Animate(ref charms.BarMotion, view.CharmsFrame, name, offsetScale: BarTravel(charms));
        Animate(
            ref charms.ClockMotion, view.CharmsClockFrame, name,
            offsetScale: -PanelTravel(CharmsBar.ClockWidth));
        if (!visible && !keepPane && charms.OpenPane != Charm.None)
        {
            charms.ClosingPane = true;
            Animate(
                ref charms.PaneMotion, view.CharmsPaneFrame, Animation.HidePanel,
                offsetScale: PanelTravel(CharmsBar.PaneWidth));
        }

        UpdateClock(view);
        charms.Draw();
        BasinReport.Line($"CHARMS {(visible ? "on" : "off")}");
        UpdateDim(view, charms);
        if (!charms.BarMotion.IsRunning && !charms.ClockMotion.IsRunning && !charms.PaneMotion.IsRunning)
        {
            SettleCharms(view, charms);
        }
    }

    internal void ClosePane(ShellView view)
    {
        if (view.Charms is not { } charms || charms.OpenPane == Charm.None || charms.ClosingPane)
        {
            return;
        }

        charms.ClosingPane = true;
        Animate(
            ref charms.PaneMotion, view.CharmsPaneFrame, Animation.HidePanel,
            offsetScale: PanelTravel(CharmsBar.PaneWidth));
        BasinReport.Line($"PANE off {charms.OpenPane}");
        UpdateDim(view, charms);
        if (!charms.PaneMotion.IsRunning)
        {
            SettleCharms(view, charms);
        }
    }

    private bool DimWanted(ShellView view) =>
        AnyTransient(view) || view.Charms is { AnyVisible: true };

    private static void ArmDim(ShellView view)
    {
        if (view.DimRect is not { } dim)
        {
            return;
        }

        dim.Width = view.Box.Width;
        dim.Height = view.Box.Height;
        dim.Enabled = true;
        view.Dim.Enabled = true;
    }

    private void SettleCharms(ShellView view, CharmsBar charms)
    {
        if (!charms.Visible && !charms.BarMotion.IsRunning && !charms.ClockMotion.IsRunning)
        {
            charms.Retire();
            Tween.Reset(view.CharmsFrame);
            Tween.Reset(view.CharmsClockFrame);
        }

        if (charms.ClosingPane && !charms.PaneMotion.IsRunning)
        {
            charms.RetirePane();
            Tween.Reset(view.CharmsPaneFrame);
        }

        UpdateDim(view, charms);
        if (charms.AnyVisible)
        {
            return;
        }

        if (view.DimRect is { } dim)
        {
            dim.Enabled = false;
        }

        view.Dim.Enabled = DimWanted(view);
    }

    private static void UpdateDim(ShellView view, CharmsBar charms)
    {
        if (charms.Visible)
        {
            view.DimFrame.Alpha = charms.BarMotion.IsRunning ? DimFor(charms.BarMotion) : 1f;
            return;
        }

        if (charms.OpenPane != Charm.None && !charms.ClosingPane)
        {
            view.DimFrame.Alpha = 1f;
            return;
        }

        if (charms.PaneMotion.IsRunning)
        {
            view.DimFrame.Alpha = DimFor(charms.PaneMotion);
            return;
        }

        view.DimFrame.Alpha = charms.BarMotion.IsRunning ? DimFor(charms.BarMotion) : 1f;
    }

    internal void ToggleCharms(ShellView view) => ShowCharms(view, view.Charms is not { Visible: true });

    internal Charm CharmAt(ShellView view, double localX, double localY) =>
        view.Charms is { Visible: true } charms ? charms.CharmAt(localX, localY) : Charm.None;

    internal bool ActivateCharm(ShellView view, Charm charm)
    {
        if (view.Charms is not { Visible: true } charms || charm == Charm.None)
        {
            return false;
        }

        if (charm == Charm.Start)
        {
            ShowCharms(view, false);
            ToggleStart(view);
            return true;
        }

        if (charms.OpenPane == charm)
        {
            ShowCharms(view, false);
            return true;
        }

        charms.ShowPane(charm);
        charms.ClosingPane = false;
        ArmDim(view);
        Animate(
            ref charms.PaneMotion, view.CharmsPaneFrame, Animation.ShowPanel,
            offsetScale: PanelTravel(CharmsBar.PaneWidth));
        charms.Draw();
        BasinReport.Line($"CHARM {charm}");
        ShowCharms(view, false, keepPane: true);
        return true;
    }

    private void UpdateClock(ShellView view)
    {
        if (view.Charms is not { } charms)
        {
            return;
        }

        var now = DateTime.Now;
        charms.Clock = now.ToString("H:mm", CultureInfo.InvariantCulture);
        charms.Date = now.ToString("dddd, d MMMM", CultureInfo.InvariantCulture);
    }

    private void AdvanceCharms(ShellView view, long nowMillis)
    {
        if (view.Charms is not { } charms)
        {
            return;
        }

        var moved = false;
        if (charms.BarMotion.IsRunning)
        {
            charms.BarMotion.Advance(nowMillis);
            charms.BarMotion.Apply(view.CharmsFrame);
            moved = true;
        }

        if (charms.ClockMotion.IsRunning)
        {
            charms.ClockMotion.Advance(nowMillis);
            charms.ClockMotion.Apply(view.CharmsClockFrame);
            moved = true;
        }

        if (charms.PaneMotion.IsRunning)
        {
            charms.PaneMotion.Advance(nowMillis);
            charms.PaneMotion.Apply(view.CharmsPaneFrame);
            moved = true;
        }

        if (!moved)
        {
            return;
        }

        UpdateDim(view, charms);
        if (!charms.BarMotion.IsRunning && !charms.ClockMotion.IsRunning && !charms.PaneMotion.IsRunning)
        {
            SettleCharms(view, charms);
        }
    }

    private static double BarTravel(CharmsBar charms) => PanelTravel(
        charms.OpenPane == Charm.None ? CharmsBar.BarWidth : CharmsBar.PaneWidth);

    private static float DimFor(in Tween motion)
    {
        var full = AnimationCatalog.Of(Animation.ShowPanel).Offset.From;
        return full <= 0 ? 1f : (float)Math.Clamp(1 - (motion.Offset / full), 0, 1);
    }

    internal bool CharmsPress(ShellView view, double localX, double localY)
    {
        if (view.Charms is not { } charms || !charms.AnyVisible)
        {
            return false;
        }

        if (charms.Visible)
        {
            var charm = charms.CharmAt(localX, localY);
            if (charm != Charm.None)
            {
                ActivateCharm(view, charm);
                return true;
            }

            if (localX >= charms.BarBox.X)
            {
                return true;
            }
        }

        if (charms.OpenPane != Charm.None && !charms.ClosingPane && localX >= charms.PaneBox.X)
        {
            return true;
        }

        if (charms.Visible)
        {
            ShowCharms(view, false);
        }
        else
        {
            ClosePane(view);
        }

        return true;
    }

    internal void HoverCharms(ShellView view, double localX, double localY)
    {
        if (view.Charms is not { Visible: true } charms)
        {
            return;
        }

        var hot = charms.CharmAt(localX, localY);
        if (hot == charms.Hot)
        {
            return;
        }

        charms.Hot = hot;
        charms.Draw();
    }
}
