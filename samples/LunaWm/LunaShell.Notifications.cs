using Avalonia.Media;
using Basin;
using Basin.Audio.PipeWire;
using Basin.Notifications;

namespace LunaWm;

internal sealed partial class LunaShell
{
    private readonly Queue<uint> _banners = new();
    private readonly Queue<uint> _alerts = new();
    private NotificationServer? _notify;
    private readonly NotificationImages _notificationImages = new();
    private uint _bannerId;
    private IEventSource? _bannerTimer;
    private AudioSession? _soundSession;
    private SoundPlayer? _soundPlayer;
    private SoundClip? _bannerClip;
    private SoundClip? _alertClip;
    private bool _soundTried;

    private void StartNotifications()
    {
        _notify = NotificationServer.TryCreate(_host.Loop, new NotificationServerOptions
        {
            Replace = _config.ReplaceDaemon,
            DefaultExpireMs = _config.DefaultExpireMs,
            ServerName = "luna-wm",
            Vendor = "basin",
            ConnectTimeout = TimeSpan.FromSeconds(1),
        }, out var reason);
        if (_notify is null)
        {
            _log.Info($"notifications: not serving org.freedesktop.Notifications, {reason}");
            _report.Line("NOTIFICATIONS off");
            return;
        }

        _notify.Added += OnNotificationAdded;
        _notify.Replaced += (_, next) => OnNotificationReplaced(next);
        _notify.Closed += OnNotificationClosed;
        _notify.NameLost += () => _report.Line("NOTIFICATIONS lost");
        _report.Line($"NOTIFICATIONS on {_notify.UniqueName}");
    }

    private void StopNotifications()
    {
        _bannerTimer?.Remove();
        _bannerTimer = null;
        _notify?.Dispose();
        _notify = null;
        _notificationImages.Dispose();
        _soundPlayer?.Dispose();
        _soundPlayer = null;
        _soundSession?.Dispose();
        _soundSession = null;
    }

    private void AttachDashboard(LunaView view)
    {
        var dashboard = new DashboardModel();
        dashboard.Dismissed += () => ShowDashboard(view, false);
        dashboard.Tapped += group => ActivateGroup(view, group);
        dashboard.Swiped += group => DismissGroup(group);
        dashboard.ItemTapped += item => ActivateNotification(view, item.Id);
        dashboard.ItemSwiped += item => DismissNotification(item.Id);
        view.DashboardUi = new DashboardSurface(view.Dashboard, _ui, _chromeIndex, dashboard);
        view.DashboardUi.Snap.Jump(0);
        view.DashboardUi.Frame.Alpha = 0;

        var alert = new AlertModel();
        alert.Chosen += key => AlertChosen(view, key);
        view.Alert = new AlertSurface(view.Dashboard, _ui, _chromeIndex, alert);
        view.Alert.Fade.Jump(0);
        view.Alert.Frame.Alpha = 0;
        FillDashboard(view);
    }

    private void PlaceDashboard(LunaView view)
    {
        view.DashboardUi?.Chrome.Place(new Box(0, LunaView.StatusBarHeight, view.Box.Width, Math.Max(1, view.Box.Height - LunaView.StatusBarHeight)), view.Scale);
        if (view.Alert is { } alert)
        {
            alert.Model.SheetWidth = Math.Min(view.Box.Width, Math.Max(480, MetricsFor(view).ActiveCardWidth));
            alert.Chrome.Place(new Box(0, LunaView.StatusBarHeight, view.Box.Width, AlertSurface.Height), view.Scale);
        }
    }

    private static string GroupKey(Notification notification) =>
        notification.DesktopEntry is { Length: > 0 } entry ? entry : notification.AppName.Length > 0 ? notification.AppName : "notifications";

    private string GroupName(Notification notification)
    {
        if (notification.AppName.Length > 0)
        {
            return notification.AppName;
        }

        return notification.DesktopEntry is { Length: > 0 } entry && Desktop.Find(entry.EndsWith(".desktop", StringComparison.Ordinal) ? entry : entry + ".desktop") is { } found
            ? found.Name
            : "Notification";
    }

    private IImage? IconFor(Notification notification, int size)
    {
        if (_notificationImages.For(notification) is { } image)
        {
            return image;
        }

        if (notification.ImagePath is { Length: > 0 } imagePath && _icons.Load(FilePath(imagePath), size) is { } picture)
        {
            return picture;
        }

        if (notification.AppIcon.Length > 0 && _icons.Load(FilePath(notification.AppIcon), size) is { } icon)
        {
            return icon;
        }

        if (notification.DesktopEntry is { Length: > 0 } entry &&
            Desktop.Find(entry.EndsWith(".desktop", StringComparison.Ordinal) ? entry : entry + ".desktop") is { Icon: { } name })
        {
            return _icons.Load(name, size);
        }

        return null;
    }

    private void OnNotificationAdded(Notification notification)
    {
        _report.Line($"NOTIFY + {notification.Id} {GroupKey(notification)} {notification.Urgency.ToString().ToLowerInvariant()} {notification.Summary}");
        if (notification.Image is not null)
        {
            _report.Line($"NOTIFY image {notification.Id} {(_notificationImages.For(notification) is { } bitmap ? $"{bitmap.PixelSize.Width}x{bitmap.PixelSize.Height}" : "unreadable")}");
        }
        else if (notification.ImagePath is { Length: > 0 } imagePath)
        {
            _report.Line($"NOTIFY image {notification.Id} {(_icons.Load(FilePath(imagePath), 48) is null ? "unreadable" : "path")}");
        }
        if (notification.Urgency == NotificationUrgency.Critical)
        {
            _alerts.Enqueue(notification.Id);
            ShowNextAlert();
            PlaySound(notification, alert: true);
        }
        else
        {
            _banners.Enqueue(notification.Id);
            ShowNextBanner();
            PlaySound(notification, alert: false);
        }

        RefreshNotifications();
    }

    private void OnNotificationReplaced(Notification next)
    {
        _notificationImages.Forget(next.Id);
        _report.Line($"NOTIFY = {next.Id} {next.Summary}");
        if (_bannerId == next.Id)
        {
            SetBanner(next);
        }

        foreach (var view in _views)
        {
            if (view.Alert is { Shown: true } alert && alert.Model.Id == next.Id)
            {
                FillAlert(view, next);
            }
        }

        RefreshNotifications();
    }

    private static string FilePath(string name) =>
        name.StartsWith("file://", StringComparison.Ordinal) && Uri.TryCreate(name, UriKind.Absolute, out var uri) ? uri.LocalPath : name;

    private void OnNotificationClosed(Notification notification, NotificationCloseReason reason)
    {
        _report.Line($"NOTIFY - {notification.Id} {(int)reason}");
        if (_bannerId == notification.Id)
        {
            EndBanner();
        }

        foreach (var view in _views)
        {
            if (view.Alert is { Shown: true } alert && alert.Model.Id == notification.Id)
            {
                HideAlert(view);
                ShowNextAlert();
            }
        }

        RefreshNotifications();
        _notificationImages.Forget(notification.Id);
    }

    private void ShowNextBanner()
    {
        if (_bannerId != 0 || _notify is null)
        {
            return;
        }

        while (_banners.Count > 0)
        {
            var id = _banners.Dequeue();
            if (_notify.Find(id) is { } notification)
            {
                _bannerId = id;
                SetBanner(notification);
                _bannerTimer ??= _host.Loop.AddTimer(EndBanner);
                _bannerTimer.UpdateTimer(_config.BannerMs);
                _report.Line($"BANNER {id} {notification.Summary}");
                return;
            }
        }
    }

    private void SetBanner(Notification notification)
    {
        var text = notification.Body.Length > 0 ? $"{notification.Summary}  {notification.Body.ReplaceLineEndings(" ")}" : notification.Summary;
        var icon = IconFor(notification, 18);
        foreach (var view in _views)
        {
            if (view.Bar is { } bar)
            {
                bar.Model.Banner = text;
                bar.Model.BannerIcon = icon;
                bar.Model.BannerShown = true;
            }
        }
    }

    private void EndBanner()
    {
        _bannerTimer?.UpdateTimer(0);
        if (_bannerId == 0)
        {
            return;
        }

        _report.Line($"BANNER end {_bannerId}");
        _bannerId = 0;
        foreach (var view in _views)
        {
            if (view.Bar is { } bar)
            {
                bar.Model.BannerShown = false;
            }
        }

        ShowNextBanner();
    }

    private void ShowNextAlert()
    {
        if (_notify is null || _views.Count == 0)
        {
            return;
        }

        var view = PlacementView();
        if (view.Alert is not { } alert || alert.Shown)
        {
            return;
        }

        while (_alerts.Count > 0)
        {
            var id = _alerts.Dequeue();
            if (_notify.Find(id) is { } notification)
            {
                FillAlert(view, notification);
                alert.Shown = true;
                alert.Chrome.Enabled = true;
                alert.Chrome.InputEnabled = true;
                alert.Fade.Start(LunaMotion.StatusBarMenuFade, alert.Fade.Value, 1, Now, 1);
                view.Scheduler?.ScheduleRepaint();
                _report.Line($"ALERT {id} {notification.Summary}");
                return;
            }
        }
    }

    private void FillAlert(LunaView view, Notification notification)
    {
        if (view.Alert is not { } alert)
        {
            return;
        }

        var model = alert.Model;
        model.Id = notification.Id;
        model.Summary = notification.Summary;
        model.Body = notification.Body;
        model.Icon = IconFor(notification, 48);
        model.Actions.Clear();
        foreach (var action in notification.Actions)
        {
            if (!action.IsDefault)
            {
                model.Actions.Add(new AlertActionModel(action.Key, action.Label));
            }
        }

        if (model.Actions.Count == 0)
        {
            model.Actions.Add(new AlertActionModel(notification.HasAction(NotificationAction.DefaultKey) ? NotificationAction.DefaultKey : "", notification.HasAction(NotificationAction.DefaultKey) ? "Open" : "Dismiss"));
        }

        PlaceDashboard(view);
    }

    private void HideAlert(LunaView view)
    {
        if (view.Alert is not { Shown: true } alert)
        {
            return;
        }

        alert.Shown = false;
        alert.Chrome.InputEnabled = false;
        alert.Fade.Start(LunaMotion.StatusBarMenuFade, alert.Fade.Value, 0, Now, 1);
        view.Scheduler?.ScheduleRepaint();
        _seat.Refresh();
    }

    private void AlertChosen(LunaView view, string key)
    {
        if (view.Alert is not { } alert || _notify is null)
        {
            return;
        }

        var id = alert.Model.Id;
        _report.Line($"ALERT chose {id} {(key.Length > 0 ? key : "dismiss")}");
        if (key.Length == 0)
        {
            _notify.Dismiss(id);
            return;
        }

        if (!_notify.Invoke(id, key, _tokens.MintShell()) || _notify.Find(id) is { Resident: true })
        {
            _notify.Dismiss(id);
        }
    }

    private void RefreshNotifications()
    {
        foreach (var view in _views)
        {
            FillDashboard(view);
        }
    }

    private void FillDashboard(LunaView view)
    {
        if (view.DashboardUi is not { } dashboard)
        {
            return;
        }

        var groups = new List<DashboardGroupModel>();
        var byKey = new Dictionary<string, DashboardGroupModel>(StringComparer.Ordinal);
        var expanded = new HashSet<string>(dashboard.Model.Groups.Where(g => g.Expanded).Select(g => g.Key), StringComparer.Ordinal);
        if (_notify is not null)
        {
            for (var i = _notify.Active.Count - 1; i >= 0; i--)
            {
                var notification = _notify.Active[i];
                if (notification.Transient || (notification.Urgency == NotificationUrgency.Critical && (_alerts.Contains(notification.Id) || AlertShowing(notification.Id))))
                {
                    continue;
                }

                var key = GroupKey(notification);
                if (!byKey.TryGetValue(key, out var group))
                {
                    group = new DashboardGroupModel { Key = key, AppName = GroupName(notification), Icon = IconFor(notification, 32), Summary = notification.Summary, Body = notification.Body.Length > 0 ? notification.Body : GroupName(notification) };
                    group.Expanded = expanded.Contains(key);
                    byKey[key] = group;
                    groups.Add(group);
                }

                group.Items.Add(new DashboardItemModel { Id = notification.Id, Summary = notification.Summary, Body = notification.Body });
                group.Count = group.Items.Count;
            }
        }

        dashboard.Model.Groups.Clear();
        foreach (var group in groups)
        {
            dashboard.Model.Groups.Add(group);
        }

        dashboard.Model.Refresh();
        if (view.Bar is { } bar)
        {
            bar.Model.NotificationIcons.Clear();
            foreach (var group in groups)
            {
                bar.Model.NotificationIcons.Add(new StatusIconModel { Key = group.Key, Letter = group.Letter, Icon = group.Icon });
            }
        }
    }

    private bool AlertShowing(uint id)
    {
        foreach (var view in _views)
        {
            if (view.Alert is { Shown: true } alert && alert.Model.Id == id)
            {
                return true;
            }
        }

        return false;
    }

    internal bool DashboardOpen(LunaView view) => view.DashboardUi is { Open: true };

    internal void ShowDashboard(LunaView view, bool open)
    {
        if (view.DashboardUi is not { } dashboard || dashboard.Open == open)
        {
            return;
        }

        if (open)
        {
            CloseOtherShellUi(view, keepMenu: false);
            FillDashboard(view);
        }

        dashboard.Open = open;
        dashboard.Chrome.Enabled = true;
        dashboard.Chrome.InputEnabled = open;
        dashboard.Snap.Start(LunaMotion.DashboardSnap, dashboard.Snap.Value, open ? 1 : 0, Now, 1);
        ApplySnap(dashboard);
        view.Scheduler?.ScheduleRepaint();
        _seat.Refresh();
        _report.Line($"DASHBOARD {(open ? "on" : "off")}");
    }

    private static void ApplySnap(DashboardSurface dashboard)
    {
        dashboard.Frame.Alpha = (float)Math.Clamp(dashboard.Snap.Value * 2, 0, 1);
        dashboard.Frame.Matrix = RenderTransform.Translation(0, (dashboard.Snap.Value - 1) * 120);
    }

    private bool AdvanceNotifications(LunaView view, long now)
    {
        var animating = false;
        if (view.DashboardUi is { } dashboard && dashboard.Snap.Advance(now))
        {
            ApplySnap(dashboard);
            if (!dashboard.Snap.IsRunning && !dashboard.Open)
            {
                dashboard.Chrome.Enabled = false;
            }

            animating |= dashboard.Snap.IsRunning;
        }

        if (view.Alert is { } alert && alert.Fade.Advance(now))
        {
            alert.Frame.Alpha = (float)alert.Fade.Value;
            if (!alert.Fade.IsRunning && !alert.Shown)
            {
                alert.Chrome.Enabled = false;
            }

            animating |= alert.Fade.IsRunning;
        }

        return animating;
    }

    private void SettleNotifications(LunaView view)
    {
        if (view.DashboardUi is { } dashboard && dashboard.Snap.IsRunning)
        {
            dashboard.Snap.Settle();
            ApplySnap(dashboard);
            dashboard.Chrome.Enabled = dashboard.Open;
        }

        if (view.Alert is { } alert && alert.Fade.IsRunning)
        {
            alert.Fade.Settle();
            alert.Frame.Alpha = (float)alert.Fade.Value;
            alert.Chrome.Enabled = alert.Shown;
        }
    }

    private void ActivateGroup(LunaView view, DashboardGroupModel group)
    {
        if (group.Items.Count > 0)
        {
            ActivateNotification(view, group.Items[0].Id);
        }
    }

    private void ActivateNotification(LunaView view, uint id)
    {
        if (_notify?.Find(id) is not { } notification)
        {
            return;
        }

        ShowDashboard(view, false);
        if (notification.HasAction(NotificationAction.DefaultKey))
        {
            _notify.Invoke(id, NotificationAction.DefaultKey, _tokens.MintShell());
            _report.Line($"NOTIFY invoke {id} default");
            return;
        }

        if (CardForNotification(notification) is { } card)
        {
            Show(card);
            _report.Line($"NOTIFY show {id} {card.Id}");
        }

        _notify.Dismiss(id);
    }

    private Card? CardForNotification(Notification notification)
    {
        var entry = notification.DesktopEntry ?? "";
        foreach (var card in _cards)
        {
            if (!card.Mapped || card.IsTransient)
            {
                continue;
            }

            if ((entry.Length > 0 && (card.AppId == entry || card.AppId == entry.Replace(".desktop", "", StringComparison.Ordinal))) ||
                (notification.AppName.Length > 0 && string.Equals(card.AppId, notification.AppName, StringComparison.OrdinalIgnoreCase)))
            {
                return card;
            }
        }

        return null;
    }

    private void DismissGroup(DashboardGroupModel group)
    {
        foreach (var item in group.Items.ToArray())
        {
            DismissNotification(item.Id);
        }
    }

    private void DismissNotification(uint id)
    {
        if (_notify?.Dismiss(id) == true)
        {
            _report.Line($"NOTIFY dismiss {id}");
        }
        else
        {
            _report.Line($"NOTIFY no {id}");
        }
    }

    private void PostNotification(string summary, string body)
    {
        if (_notify is null)
        {
            _report.Line("NOTIFY unavailable");
            return;
        }

        var id = _notify.Post(new Notification { AppName = "luna-wm", Summary = summary, Body = body });
        _report.Line($"NOTIFY posted {id}");
    }

    private void PlaySound(Notification notification, bool alert)
    {
        var kind = alert ? "alert" : "banner";
        if (!_config.Sound || notification.SuppressSound)
        {
            _report.Line($"SOUND {kind} off");
            return;
        }

        if (Environment.GetEnvironmentVariable("BASIN_MUTE") == "1")
        {
            _report.Line($"SOUND {kind} muted");
            return;
        }

        EnsureSounds();

        if (_soundPlayer is null || (alert ? _alertClip : _bannerClip) is not { } clip)
        {
            _report.Line($"SOUND {kind} unavailable");
            return;
        }

        _soundPlayer.Play(clip, SoundPlacement.Centered);
        _report.Line($"SOUND {kind}");
    }

    private bool EnsureSounds()
    {
        if (!_soundTried)
        {
            _soundTried = true;
            _soundSession = AudioSession.TryCreate(_host.Loop, out var reason, clientName: "luna-wm-sounds");
            if (_soundSession is not null)
            {
                _soundPlayer = SoundPlayer.Create(_soundSession, $"luna-wm.{Environment.ProcessId}.sounds", out reason, role: "Notification");
                _bannerClip = LoadClip("glass_002");
                _alertClip = LoadClip("question_001");
            }

            if (_soundPlayer is null)
            {
                _log.Info($"notifications: no sounds, {reason}");
            }
        }

        return _soundPlayer is not null;
    }

    private static SoundClip? LoadClip(string name)
    {
        using var stream = typeof(LunaShell).Assembly.GetManifestResourceStream($"Kenney/{name}.wav");
        return stream is null ? null : SoundClip.FromWav(name, stream);
    }

    private void PrintNotificationState(LunaView view)
    {
        _report.Line($"  notifications {(_notify is null ? "off" : $"active={_notify.Active.Count}")} banner={(_bannerId == 0 ? "-" : _bannerId.ToString(System.Globalization.CultureInfo.InvariantCulture))} dashboard={(DashboardOpen(view) ? "open" : "closed")} alert={(view.Alert is { Shown: true } alert ? alert.Model.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-")}");
        if (view.DashboardUi is { } dashboard)
        {
            foreach (var group in dashboard.Model.Groups)
            {
                _report.Line($"  group {group.Key} count={group.Count} {group.Summary}");
            }
        }
    }
}
