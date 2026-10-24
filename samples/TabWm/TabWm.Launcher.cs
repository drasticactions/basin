using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Basin;
using Basin.Diagnostics;
using Basin.Freedesktop;
using Basin.Scene;
using Basin.UI.Avalonia;
using TabWm.Chrome;

namespace TabWm;

internal sealed partial class TabWm
{
    private static readonly IReadOnlySet<string> CurrentDesktopNames = new HashSet<string>(StringComparer.Ordinal) { "tab-wm", "basin", "KDE" };

    private readonly Dictionary<string, (string? Name, IImage? Image)> _icons = new(StringComparer.Ordinal);
    private readonly List<LauncherViewModel> _launchers = [];
    private readonly Dictionary<TabSet, (LauncherViewModel Model, LauncherView View)> _pages = [];
    private DesktopEntries? _desktop;
    private DirectoryWatch? _desktopWatch;
    private LauncherModel? _launcher;
    private TabOverlay? _overlay;

    private IconSearch? _iconSearchCache;

    private DesktopEntries Desktop => _desktop ??= new DesktopEntries();

    private IconSearch IconSearcher => _iconSearchCache ??= new IconSearch { Entries = Desktop, Extensions = [".png", ".svg"] };

    private LauncherModel Launcher => _launcher ??= CreateLauncherModel();

    private LauncherModel CreateLauncherModel()
    {
        var saved = StateFiles.Read(StateFiles.LauncherPath, TabWmJsonContext.Default.LauncherFile, out var error);
        if (error is not null)
        {
            _log.Warn($"launcher: cannot read {error}, the pinned seed is used");
        }

        var model = new LauncherModel(saved?.Pinned ?? (IEnumerable<string>)_config.Pinned);
        model.Changed += RefreshLaunchers;
        return model;
    }

    private void StartLauncher()
    {
        _desktopWatch = new DirectoryWatch(_host.Loop);
        Desktop.Watch(_desktopWatch);
        Desktop.Changed += LoadLauncher;
        LoadLauncher();
    }

    private void StopLauncher()
    {
        Desktop.Watch(null);
        _desktopWatch?.Dispose();
        _desktopWatch = null;
    }

    private void LoadLauncher()
    {
        _icons.Clear();
        var items = new List<LauncherItem>();
        foreach (var entry in Desktop.Listable(CurrentDesktopNames))
        {
            var exec = entry.Argv.Count > 0 ? Path.GetFileName(entry.Argv[0]) : string.Empty;
            var category = DesktopCategories.DefaultLabel(DesktopCategories.MainOf(entry));
            items.Add(new LauncherItem(entry.Id, entry.Name, entry.GenericName, entry.Icon, entry.Keywords, exec, category));
        }

        Launcher.Load(items);
        BasinReport.Line($"LAUNCHER entries={items.Count} pinned={Launcher.PinnedItems().Count}");
        foreach (var set in _sets)
        {
            foreach (var tab in set.Tabs)
            {
                RefreshTab(tab);
            }
        }
    }

    private void RefreshLaunchers()
    {
        foreach (var view in _launchers)
        {
            Fill(view);
        }
    }

    private void Fill(LauncherViewModel view)
    {
        var model = Launcher;
        view.SetPinned(model.PinnedItems().Select(EntryModel));
        view.Categories.Clear();
        foreach (var (category, items) in model.Categories())
        {
            view.Categories.Add(new LauncherCategoryModel(category, items.Select(EntryModel).ToList()));
        }

        if (view.Searching)
        {
            Search(view, view.SearchText);
        }
    }

    private LauncherEntryModel EntryModel(LauncherItem item)
    {
        var (name, image) = IconOf(item.Icon, 48);
        return new LauncherEntryModel(item.Id, item.Name, name ?? "application-x-executable", image) { IsPinned = Launcher.IsPinned(item.Id) };
    }

    private void Search(LauncherViewModel view, string text)
    {
        var results = Launcher.Search(text).Select(EntryModel).ToList();
        if (results.Count == 0 && text.Trim().Length > 0)
        {
            results.Add(new LauncherEntryModel("run", $"Run “{text.Trim()}”", "system-run", null, run: true));
        }

        view.SetResults(results);
    }

    private LauncherViewModel CreateLauncherViewModel(Func<Tab?> target, bool overlay)
    {
        var view = new LauncherViewModel { IsOverlay = overlay };
        view.Searched = text => Search(view, text);
        view.Launch = entry =>
        {
            var tab = target();
            if (overlay)
            {
                CloseOverlay();
            }

            view.Reset();
            if (Desktop.Find(entry.Id) is { } desktop)
            {
                LaunchEntry(desktop, tab);
            }
        };
        view.Run = text =>
        {
            var tab = target();
            if (overlay)
            {
                CloseOverlay();
            }

            view.Reset();
            if (text.Length > 0)
            {
                Launch(tab, text, null, text);
            }
        };
        view.Pin = (id, pin) =>
        {
            if (pin ? Launcher.Pin(id) : Launcher.Unpin(id))
            {
                SaveLauncherState();
            }
        };
        view.MovePinned = (id, index) =>
        {
            if (Launcher.MovePinned(id, index))
            {
                SaveLauncherState();
            }
        };
        view.Dismiss = () =>
        {
            if (overlay)
            {
                CloseOverlay();
            }
        };
        _launchers.Add(view);
        Fill(view);
        return view;
    }

    private void SaveLauncherState()
    {
        var file = new LauncherFile { Pinned = [.. Launcher.Pinned] };
        if (!StateFiles.Write(StateFiles.LauncherPath, file, TabWmJsonContext.Default.LauncherFile, out var error))
        {
            _log.Warn($"launcher: cannot save {error}");
        }
    }

    private LauncherView CreatePage(TabSet set)
    {
        var model = CreateLauncherViewModel(() => SetOf(set.Output)?.Active, overlay: false);
        var view = new LauncherView { DataContext = model };
        _pages[set] = (model, view);
        return view;
    }

    private void FocusPage(TabSet set)
    {
        if (_pages.TryGetValue(set, out var page))
        {
            page.View.FocusSearch();
        }
    }

    internal void ShowOverlay(TabSet set)
    {
        var model = CreateLauncherViewModel(() => SetOf(set.Output)?.Active, overlay: true);
        var view = new LauncherView { DataContext = model };
        if (OpenOverlay(set, view, model, 900, 640))
        {
            view.FocusSearch();
            BasinReport.Line($"LAUNCHER overlay shown output={set.Output.Name}");
        }
        else
        {
            _launchers.Remove(model);
        }
    }

    private bool OpenOverlay(TabSet set, Control view, LauncherViewModel? model, int maxWidth, int maxHeight)
    {
        CloseOverlay();
        var work = set.Work;
        var width = Math.Min(maxWidth, Math.Max(320, work.Width - 80));
        var height = Math.Min(maxHeight, Math.Max(240, work.Height - 80));
        var box = new Box(work.X + ((work.Width - width) / 2), work.Y + ((work.Height - height) / 2), width, height);
        if (_ui.CreateSurface(new Basin.Capabilities.UISurfaceOptions { Target = _ui.Produces, Width = width, Height = height, Scale = set.Output.Scale }) is not AvaloniaUISurface surface)
        {
            return false;
        }

        var dim = new SceneRect(_layers.Launcher, work.Width, work.Height, new RenderColor(0f, 0f, 0f, 0.4f));
        dim.SetPosition(work.X, work.Y);
        surface.Content = view;
        surface.SetPosition(box.X, box.Y);
        var node = new UISurfaceNode(_layers.Launcher, surface, _index) { PreciseDamage = true };
        node.SetPosition(box.X, box.Y);
        _overlay = new TabOverlay(set, surface, node, dim, box, view, model);
        KeyboardTarget = surface;
        Seat.Keyboard.NotifyClearFocus();
        _outputs.ScheduleAll();
        return true;
    }

    internal void CloseOverlay()
    {
        if (_overlay is not { } overlay)
        {
            return;
        }

        _overlay = null;
        if (overlay.Model is { } model)
        {
            _launchers.Remove(model);
        }

        if (ReferenceEquals(KeyboardTarget, overlay.Surface))
        {
            KeyboardTarget = null;
        }

        overlay.Dispose();
        BasinReport.Line(overlay.Model is null ? "SETTINGS hidden" : "LAUNCHER overlay hidden");
        if (!_stopping)
        {
            RestoreFocus();
        }

        _outputs.ScheduleAll();
    }

    private (string? Name, IImage? Image) IconFor(string appId)
    {
        if (appId.Length == 0)
        {
            return ("application-x-executable", null);
        }

        if (Desktop.FindForAppId(appId) is { Icon: { Length: > 0 } icon })
        {
            var (name, image) = IconOf(icon, 16);
            return (name ?? "application-x-executable", image);
        }

        if (IconSearcher.Find(appId) is { } path)
        {
            var (_, image) = IconOf(path, 16);
            if (image is not null)
            {
                return (null, image);
            }
        }

        return ("application-x-executable", null);
    }

    private (string? Name, IImage? Image) IconOf(string? icon, int size)
    {
        if (icon is not { Length: > 0 })
        {
            return (null, null);
        }

        if (!Path.IsPathRooted(icon))
        {
            return (icon, null);
        }

        var key = $"{size}:{icon}";
        if (_icons.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var result = (Name: (string?)null, Image: Raster(icon, size));
        _icons[key] = result;
        return result;
    }

    private static unsafe IImage? Raster(string path, int size)
    {
        if (Basin.Icons.Skia.IconRasterizer.Argb(path, size) is not { } argb)
        {
            return null;
        }

        var bitmap = new WriteableBitmap(new PixelSize(size, size), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using (var locked = bitmap.Lock())
        {
            var count = Math.Min(argb.Length, size * size);
            argb.AsSpan(0, count).CopyTo(new Span<uint>((void*)locked.Address, count));
        }

        return bitmap;
    }
}
