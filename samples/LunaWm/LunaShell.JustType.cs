using Basin;
using Basin.Config;
using Basin.Freedesktop;

namespace LunaWm;

internal sealed partial class LunaShell
{
    internal const int MaxAppResults = 8;
    internal const int MaxCardResults = 5;

    private void AttachJustType(LunaView view)
    {
        var model = new JustTypeModel();
        model.QueryChanged += query => FillJustType(view, model, query);
        model.Chosen += row => JustTypeChosen(view, row);
        model.Dismissed += () => CloseJustType(view);
        view.JustType = new JustTypeSurface(view.Launcher, _ui, _chromeIndex, model);
        view.JustType.Fade.Jump(0);
        view.JustType.Frame.Alpha = 0;
        PlaceJustType(view);
    }

    private void PlaceJustType(LunaView view) =>
        view.JustType?.Chrome.Place(new Box(0, LunaView.StatusBarHeight, view.Box.Width, Math.Max(1, view.Box.Height - LunaView.StatusBarHeight)), view.Scale);

    internal bool JustTypeOpen(LunaView view) => view.JustType is { Open: true };

    private bool JustTypeKey(LunaView view, uint key, uint keysym, Modifiers held)
    {
        if (_locked || JustTypeOpen(view) || (held & ~Modifiers.Shift) != Modifiers.None)
        {
            return false;
        }

        if (!(view.InCardView || view.Deck.IsEmpty || LauncherOpen(view)) || MenuOpen(view) || DashboardOpen(view))
        {
            return false;
        }

        Span<char> text = stackalloc char[8];
        var length = Seat.Keyboard.TextFor(key, text);
        if (length != 1 || char.IsControl(text[0]) || char.IsWhiteSpace(text[0]))
        {
            return false;
        }

        OpenJustType(view, text[..length].ToString());
        return true;
    }

    private void OpenJustType(LunaView view, string text)
    {
        if (view.JustType is not { } justType)
        {
            return;
        }

        var fromLauncher = LauncherOpen(view);
        CloseMenu(view);
        ShowDashboard(view, false);
        if (fromLauncher)
        {
            ShowLauncher(view, false);
        }

        justType.Model.Query = text;
        FillJustType(view, justType.Model, text);
        if (!justType.Open)
        {
            justType.Open = true;
            justType.Chrome.Enabled = true;
            justType.Chrome.InputEnabled = true;
            justType.Fade.Start(LunaMotion.UniversalSearchCrossFade, justType.Fade.Value, 1, Now, 1);
            justType.Frame.Alpha = (float)justType.Fade.Value;
        }

        if (justType.Chrome.Surface is { } surface)
        {
            _seat.Router.SetKeyboardFocus(surface);
        }

        Avalonia.Threading.Dispatcher.UIThread.Post(justType.View.FocusField, Avalonia.Threading.DispatcherPriority.Background);
        SyncDock(view, animate: true);
        view.Scheduler?.ScheduleRepaint();
        _report.Line($"JUSTTYPE on {text}");
    }

    private void CloseJustType(LunaView view)
    {
        if (view.JustType is not { Open: true } justType)
        {
            return;
        }

        justType.Open = false;
        justType.Chrome.InputEnabled = false;
        justType.Fade.Start(LunaMotion.UniversalSearchCrossFade, justType.Fade.Value, 0, Now, 1);
        _seat.ClearChromeFocus();
        SyncDock(view, animate: true);
        view.Scheduler?.ScheduleRepaint();
        _report.Line("JUSTTYPE off");
    }

    private bool AdvanceJustType(LunaView view, long now)
    {
        if (view.JustType is not { } justType || !justType.Fade.Advance(now))
        {
            return false;
        }

        justType.Frame.Alpha = (float)justType.Fade.Value;
        if (!justType.Fade.IsRunning && !justType.Open)
        {
            justType.Chrome.Enabled = false;
        }

        return justType.Fade.IsRunning;
    }

    private void SettleJustType(LunaView view)
    {
        if (view.JustType is { } justType && justType.Fade.IsRunning)
        {
            justType.Fade.Settle();
            justType.Frame.Alpha = (float)justType.Fade.Value;
            justType.Chrome.Enabled = justType.Open;
        }
    }

    internal static IEnumerable<(LauncherEntry Entry, int Rank)> MatchApps(IEnumerable<LauncherEntry> entries, string query)
    {
        foreach (var entry in entries)
        {
            var rank = Rank(entry.Name, query);
            if (entry.GenericName is { } generic)
            {
                rank = Math.Min(rank, Rank(generic, query) + 1);
            }

            foreach (var keyword in entry.Keywords)
            {
                rank = Math.Min(rank, Rank(keyword, query) + 2);
            }

            if (rank < int.MaxValue / 2)
            {
                yield return (entry, rank);
            }
        }
    }

    private static int Rank(string text, string query)
    {
        var index = text.IndexOf(query, StringComparison.CurrentCultureIgnoreCase);
        if (index < 0)
        {
            return int.MaxValue / 2;
        }

        if (index == 0)
        {
            return 0;
        }

        for (var at = index; at >= 0; at = text.IndexOf(query, at + 1, StringComparison.CurrentCultureIgnoreCase))
        {
            if (!char.IsLetterOrDigit(text[at - 1]))
            {
                return 1;
            }
        }

        return 3;
    }

    private void FillJustType(LunaView view, JustTypeModel model, string query)
    {
        var rows = new List<JustTypeRow>();
        query = query.Trim();
        if (query.Length > 0)
        {
            var apps = MatchApps(_entries, query)
                .OrderBy(match => match.Rank)
                .ThenBy(match => match.Entry.Name, StringComparer.CurrentCultureIgnoreCase)
                .Take(MaxAppResults)
                .ToList();
            if (apps.Count > 0)
            {
                rows.Add(new JustTypeRow { Kind = "header", Label = "Apps", IsHeader = true });
                foreach (var (entry, _) in apps)
                {
                    rows.Add(new JustTypeRow { Kind = "app", Label = entry.Name, Key = entry.Id, Detail = entry.GenericName ?? "", Icon = _icons.Load(entry.Icon, 32) });
                }
            }

            var cards = new List<JustTypeRow>();
            foreach (var card in _cards)
            {
                if (!card.Mapped || card.IsTransient || card.Closing)
                {
                    continue;
                }

                if (card.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase) || card.AppId.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                {
                    cards.Add(new JustTypeRow { Kind = "card", Label = card.Label, Key = card.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), Detail = card.AppId });
                    if (cards.Count >= MaxCardResults)
                    {
                        break;
                    }
                }
            }

            if (cards.Count > 0)
            {
                rows.Add(new JustTypeRow { Kind = "header", Label = "Cards", IsHeader = true });
                rows.AddRange(cards);
            }

            if (_config.JustTypeRun || _config.JustTypeWeb)
            {
                rows.Add(new JustTypeRow { Kind = "header", Label = "Actions", IsHeader = true });
                if (_config.JustTypeRun)
                {
                    rows.Add(new JustTypeRow { Kind = "run", Label = $"Run \"{query}\"", Key = query });
                }

                if (_config.JustTypeWeb)
                {
                    rows.Add(new JustTypeRow { Kind = "web", Label = $"Search the web for \"{query}\"", Key = query });
                }
            }
        }

        model.SetRows(rows);
    }

    private void JustTypeChosen(LunaView view, JustTypeRow row)
    {
        _report.Line($"JUSTTYPE chose {row.Kind} {row.Key}");
        switch (row.Kind)
        {
            case "app":
                CloseJustType(view);
                LaunchEntry(view, row.Key);
                break;
            case "card":
                CloseJustType(view);
                foreach (var card in _cards)
                {
                    if (card.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) == row.Key)
                    {
                        Show(card);
                        break;
                    }
                }

                break;
            case "run":
                CloseJustType(view);
                Launch(row.Key);
                break;
            case "web":
                CloseJustType(view);
                SearchWeb(row.Key);
                break;
        }
    }

    internal static string SearchAddress(string template, string query) =>
        template.Contains("%s", StringComparison.Ordinal)
            ? template.Replace("%s", Uri.EscapeDataString(query), StringComparison.Ordinal)
            : template + Uri.EscapeDataString(query);

    private void SearchWeb(string query) => OpenUri(SearchAddress(_config.SearchUrl, query), "web search");

    private void OpenUri(string uri, string label)
    {
        var colon = uri.IndexOf(':');
        var scheme = colon > 0 ? uri[..colon] : "https";
        if (MimeApps.DefaultFor($"x-scheme-handler/{scheme}") is { } id && Desktop.Find(id) is { } handler &&
            handler.LaunchArgv(ExecLine.TerminalFromEnvironment()) is { Length: > 0 } argv)
        {
            Launch(ExecLine.Join([.. argv, uri]), handler.Name, handler.Icon, handler.Id);
            return;
        }

        Launch(ExecLine.Join(["xdg-open", uri]), $"xdg-open {label}");
    }

    private void TypeCommand(LunaView view, string text)
    {
        if (!JustTypeOpen(view))
        {
            OpenJustType(view, text);
        }
        else if (view.JustType is { } justType)
        {
            justType.Model.Query = text;
        }

        if (view.JustType is { } shown)
        {
            _report.Line($"JUSTTYPE rows={string.Join(',', shown.Model.Rows.Select(r => r.IsHeader ? $"[{r.Label}]" : r.Label))}");
        }
    }

    private void JustTypeChoose(LunaView view, int index)
    {
        if (view.JustType is not { Open: true } justType)
        {
            _report.Line("JUSTTYPE closed");
            return;
        }

        var results = justType.Model.Rows.Where(r => r.IsResult).ToList();
        if (index < 0 || index >= results.Count)
        {
            _report.Line($"JUSTTYPE no {index}");
            return;
        }

        justType.Model.Choose(results[index]);
    }
}
