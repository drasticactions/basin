using System7.Avalonia.Rendering;
using System7.Avalonia;
using Avalonia.Controls;
using Avalonia;
using Basin.Config;
using Basin.Diagnostics;
using Basin.Freedesktop;
using Basin.Host;
using Basin.Scene;
using Basin;
using Point = Avalonia.Point;

namespace System7Wm;

internal sealed partial class System7Shell
{
    private readonly MimeGlobs _mime = new();
    private readonly Dictionary<string, FinderView> _finderViews = new(StringComparer.Ordinal);
    private FreedesktopTrash? _trash;
    private ChromeSurface? _desktopSurface;
    private DesktopView? _desktop;
    private DirectoryWatch? _watch;
    private (IReadOnlyList<string> Paths, bool Copy, IReadOnlyList<(string Name, Vector Offset)> Grab)? _fileDrag;
    private ZoomRects? _zoom;

    internal FreedesktopTrash Trash => _trash ??= new FreedesktopTrash();

    internal DesktopView? Desktop => _desktop;

    private static string DesktopFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop");

    private static string StateFile => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_STATE_HOME") is { Length: > 0 } state && Path.IsPathRooted(state)
            ? state
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state"),
        "system7-wm", "desktop.toml");

    private DirectoryWatch Watch
    {
        get
        {
            if (_watch is null)
            {
                _watch = new DirectoryWatch(_host.Loop);
                _watch.Changed += OnFolderChanged;
            }

            return _watch;
        }
    }

    private void RelayoutDesktop()
    {
        if (MainView is not { } view)
        {
            return;
        }

        if (_desktopSurface is null)
        {
            _desktop = new DesktopView { Depth = _config.Depth };
            _desktop.Opened += OpenIcon;
            _desktop.Moved += SaveDesktopPositions;
            _desktop.Renamed += (icon, name) =>
            {
                if (icon.Kind == DesktopIconKind.Disk)
                {
                    _config.SetDiskName(name);
                    SaveSetting("finder", "disk_name", TomlValue.From(name));
                    LoadDesktopIcons();
                    BasinReport.Line($"RENAME disk {name}");
                    return;
                }

                if (icon.Kind is DesktopIconKind.File or DesktopIconKind.Folder)
                {
                    Rename(icon.Path, name);
                }
            };
            _desktop.Dropped += OnDesktopDrop;
            _desktop.Pressed += () =>
            {
                ActivateFinder();
                if (_desktopSurface?.Surface is { } surface)
                {
                    _seat.Router.SetKeyboardFocus(surface);
                }
            };
            _desktop.OverWindowTest = point => WindowAt(DesktopToScene(point).X, DesktopToScene(point).Y, out _, out _, out _) is not null;
            _desktopSurface = new ChromeSurface(_layers.Icons, _ui, _chrome, () => _desktop);
            Watch.Add(DesktopFolder);
            try
            {
                Directory.CreateDirectory(Trash.FilesDirectory);
                Directory.CreateDirectory(Trash.InfoDirectory);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                _log.Warn(error, $"finder: cannot create {Trash.FilesDirectory}");
            }

            Watch.Add(Trash.FilesDirectory);
            Watch.Add(Trash.InfoDirectory);
            BuildFinderMenus();
        }

        var box = _layout.BoxOf(view.Output);
        var scale = view.Output.Scale;
        var (deviceWidth, deviceHeight) = DevicePlacement.OutputSize(view.Output);
        _desktopSurface.Place(box.X, box.Y, scale, new Box(0, 0, deviceWidth, deviceHeight));
        LoadDesktopIcons();
    }

    private Point DesktopToScene(Point point)
    {
        if (MainView is not { } view)
        {
            return point;
        }

        var box = _layout.BoxOf(view.Output);
        var scale = view.Output.Scale;
        var n = DevicePlacement.ChromeScale(scale);
        return new Point(box.X + (point.X * n / scale), box.Y + (point.Y * n / scale));
    }

    private void ApplyDesktopDepth()
    {
        if (_desktop is { } desktop)
        {
            desktop.Depth = _config.Depth;
            desktop.Refresh();
            System7Theme.SetColorDepth(desktop, _config.Depth);
        }

        foreach (var window in _stack.Where(w => w.IsShell))
        {
            if (window.ShellFrame is { } frame)
            {
                System7Theme.SetColorDepth(frame, _config.Depth);
            }
        }
    }

    private void LoadDesktopIcons()
    {
        if (_desktop is not { } desktop || _desktopSurface is null)
        {
            return;
        }

        var width = _desktopSurface.DipWidth;
        var height = _desktopSurface.DipHeight;
        var saved = ReadPositions();
        var icons = new List<DesktopIcon>
        {
            new()
            {
                Key = "disk",
                Path = _config.DiskRoot,
                Label = _config.DiskLabel,
                Family = System7IconFamily.Generic("hard-disk")!,
                Kind = DesktopIconKind.Disk,
                Position = saved.GetValueOrDefault(":disk", new Point(width - 80, 28)),
            },
        };
        var slot = 1;
        if (Directory.Exists(DesktopFolder))
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(DesktopFolder).OrderBy(p => p, StringComparer.Ordinal))
            {
                var name = Path.GetFileName(path);
                if (name.StartsWith('.') || FinderEntry.Read(path, _mime) is not { } entry)
                {
                    continue;
                }

                var column = slot / Math.Max(1, (height - 120) / 64);
                var row = slot % Math.Max(1, (height - 120) / 64);
                icons.Add(new DesktopIcon
                {
                    Key = name,
                    Path = path,
                    Label = entry.Name.EndsWith(".desktop", StringComparison.Ordinal) ? DesktopName(path) : entry.Name,
                    Family = IconForEntry(entry),
                    Kind = entry.IsDirectory ? DesktopIconKind.Folder : DesktopIconKind.File,
                    Position = saved.GetValueOrDefault(name, new Point(width - 80 - (column * 72), 28 + (row * 64))),
                });
                slot++;
            }
        }

        icons.Add(new DesktopIcon
        {
            Key = "trash",
            Path = Trash.FilesDirectory,
            Label = "Trash",
            Family = System7IconFamily.Generic(Trash.IsEmpty ? "trash" : "trash-full")!,
            Kind = DesktopIconKind.Trash,
            Position = saved.GetValueOrDefault(":trash", new Point(width - 80, height - 60)),
        });
        var selected = desktop.Selection.Select(i => i.Key).ToHashSet();
        foreach (var icon in icons)
        {
            icon.Selected = selected.Contains(icon.Key);
        }

        desktop.SetIcons(icons);
    }

    private string DesktopName(string path) =>
        DesktopEntryReader.Parse(path, Path.GetFileName(path), DesktopLocale.None)?.Name ?? Path.GetFileNameWithoutExtension(path);

    private Dictionary<string, Point> ReadPositions()
    {
        var result = new Dictionary<string, Point>(StringComparer.Ordinal);
        if (!File.Exists(StateFile) || TomlConfig.Read(StateFile, out _) is not { } table || table.TryGetValue("positions", out var positions) is false || positions is not Tomlyn.Model.TomlTable entries)
        {
            return result;
        }

        foreach (var (key, value) in entries)
        {
            if (value is Tomlyn.Model.TomlArray { Count: 2 } pair && pair[0] is long x && pair[1] is long y)
            {
                result[key] = new Point(x, y);
            }
        }

        return result;
    }

    private void SaveDesktopPositions()
    {
        if (_desktop is not { } desktop)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StateFile)!);
            var document = File.Exists(StateFile) ? TomlDocument.Load(StateFile) : TomlDocument.Parse("");
            foreach (var icon in desktop.Icons)
            {
                var key = icon.Kind switch
                {
                    DesktopIconKind.Disk => ":disk",
                    DesktopIconKind.Trash => ":trash",
                    _ => icon.Key,
                };
                document.Set("positions", key, TomlValue.Array([TomlValue.From((long)icon.Position.X), TomlValue.From((long)icon.Position.Y)]));
            }

            document.Save(StateFile);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _log.Warn(error, $"finder: cannot save the desktop to {StateFile}");
        }
    }

    private void OnFolderChanged(string folder)
    {
        if (folder == DesktopFolder || folder == Trash.FilesDirectory || folder == Trash.InfoDirectory)
        {
            LoadDesktopIcons();
            BuildFinderMenus();
        }

        if (_finderViews.TryGetValue(folder, out var view))
        {
            FillFinderView(view);
        }

        _outputs.ScheduleAll();
    }

    internal System7IconFamily IconForEntry(FinderEntry entry)
    {
        if (entry.IsDirectory)
        {
            return System7IconFamily.Generic("folder")!;
        }

        if (entry.Path.EndsWith(".desktop", StringComparison.Ordinal) &&
            DesktopEntryReader.Parse(entry.Path, entry.Name, DesktopLocale.None)?.Icon is { } appIcon)
        {
            return IconFor(appIcon, "application");
        }

        var mime = _mime.TypeOf(entry.Path);
        foreach (var name in _mime.IconNamesOf(mime))
        {
            if (Icons.Load(name, appId: false) is { } family)
            {
                return family;
            }
        }

        var executable = OperatingSystem.IsLinux() && File.GetUnixFileMode(entry.Path).HasFlag(UnixFileMode.UserExecute);
        return System7IconFamily.Generic(executable ? "application" : "document")!;
    }

    private void OpenIcon(DesktopIcon icon)
    {
        var picture = icon.PictureBounds;
        var topLeft = DesktopToScene(picture.TopLeft);
        var bottomRight = DesktopToScene(picture.BottomRight);
        var from = new Rect(topLeft, bottomRight);
        switch (icon.Kind)
        {
            case DesktopIconKind.Disk:
                OpenFinder(_config.DiskRoot, _config.DiskLabel, from);
                break;
            case DesktopIconKind.Trash:
                Directory.CreateDirectory(Trash.FilesDirectory);
                OpenFinder(Trash.FilesDirectory, "Trash", from);
                break;
            case DesktopIconKind.Folder:
                OpenFinder(icon.Path, icon.Label, from);
                break;
            default:
                OpenPath(icon.Path);
                break;
        }
    }

    internal ShellWindow OpenFinder(string folder, string? title = null, Rect? zoomFrom = null)
    {
        folder = Path.GetFullPath(folder);
        if (ShellWindowByKey($"finder:{folder}") is { } existing)
        {
            Activate(existing);
            return existing;
        }

        var state = FinderState.Load(folder);
        var (width, height) = state.Window is { } saved ? (Math.Max(120, saved.Width), Math.Max(80, saved.Height)) : (360, 220);
        var view = new FinderView(folder, state.View ?? _finderView, IconForEntry) { Depth = _config.Depth, LayoutWidth = width };
        view.SetPositions("icon", state.Icons);
        view.SetPositions("small", state.SmallIcons);
        ShellWindow? window = null;
        view.Opened += (entry, closeParent) =>
        {
            if (entry.IsDirectory)
            {
                OpenFinder(entry.Path, zoomFrom: window is not null && view.ItemRect(entry) is { } local ? SceneRectOf(window, local) : null);
                if (closeParent && window is not null)
                {
                    CloseShellWindow(window);
                }
            }
            else
            {
                OpenPath(entry.Path);
            }
        };
        view.DragStarted += (entries, copy) => BeginFileDrag(entries.Select(e => e.Path).ToList(), copy, view.DragGrab());
        view.Renamed += (entry, name) => Rename(entry.Path, name);
        view.Arranged += () => SaveFinderState(folder);
        _finderViews[folder] = view;
        Watch.Add(folder);
        FillFinderView(view);
        var name = title ?? (folder == Trash.FilesDirectory ? "Trash" : folder == _config.DiskRoot ? _config.DiskLabel : Path.GetFileName(folder));
        window = OpenShellWindow($"finder:{folder}", name.Length > 0 ? name : folder, view, width, height, resizable: true, scrollBars: true,
            placement: state.Window is { } box ? (box.X, box.Y) : null);
        window.ShellPlaced = () => SaveFinderState(folder);
        if (zoomFrom is { } zoom)
        {
            ZoomOpen(window, zoom);
        }

        window.ShellClosed = () =>
        {
            SaveFinderState(folder);
            _finderViews.Remove(folder);
            if (folder != DesktopFolder && folder != Trash.FilesDirectory)
            {
                Watch.Remove(folder);
            }
        };
        return window;
    }

    private Rect SceneRectOf(ShellWindow window, Rect local)
    {
        var view = ViewOf(window);
        var box = _layout.BoxOf(view.Output);
        var scale = view.Output.Scale;
        var n = DevicePlacement.ChromeScale(scale);
        var content = window.ShellDevice;
        return new Rect(
            box.X + ((content.X + (local.X * n)) / scale),
            box.Y + ((content.Y + (local.Y * n)) / scale),
            local.Width * n / scale,
            local.Height * n / scale);
    }

    private void ZoomOpen(ShellWindow window, Rect from)
    {
        var view = ViewOf(window);
        var box = _layout.BoxOf(view.Output);
        var scale = view.Output.Scale;
        var mode = view.Output.CurrentMode;
        var start = new Box(
            (int)Math.Round((from.X - box.X) * scale),
            (int)Math.Round((from.Y - box.Y) * scale),
            (int)Math.Round(from.Width * scale),
            (int)Math.Round(from.Height * scale));
        _zoom ??= new ZoomRects(_layers.Outlines, _host.Loop, _outputs.ScheduleAll);
        window.Container.Enabled = false;
        BasinReport.Line($"ZOOM {window.Id} from {start.X},{start.Y},{start.Width}x{start.Height}");
        _zoom.Start(box.X, box.Y, scale, mode.Width, mode.Height, start, FrameBoxOf(window, view), () =>
        {
            if (window.Mapped)
            {
                window.Container.Enabled = !window.App.Hidden;
                BasinReport.Line($"ZOOM {window.Id} done");
            }
        });
    }

    private void SaveFinderState(string folder)
    {
        if (!_finderViews.TryGetValue(folder, out var view))
        {
            return;
        }

        var state = FinderState.Load(folder);
        if (ShellWindowByKey($"finder:{folder}") is { Zoomed: false } window)
        {
            state.Window = new Box(window.X, window.Y, window.ContentDip.Width, window.ContentDip.Height);
        }

        state.View = view.Mode;
        Copy(view.PositionsOf("icon"), state.Icons);
        Copy(view.PositionsOf("small"), state.SmallIcons);

        try
        {
            state.Save(folder);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _log.Warn(error, $"finder: cannot save the window for {folder} to {FinderState.FileOf(folder)}");
        }
    }

    private static void Copy(IReadOnlyDictionary<string, Point> from, Dictionary<string, Point> to)
    {
        to.Clear();
        foreach (var (name, point) in from)
        {
            to[name] = point;
        }
    }

    private void CarryPosition(string folder, string from, string to)
    {
        if (_finderViews.TryGetValue(folder, out var view))
        {
            view.RenamePosition(from, to);
            SaveFinderState(folder);
        }
        else if (File.Exists(FinderState.FileOf(folder)))
        {
            var state = FinderState.Load(folder);
            foreach (var icons in new[] { state.Icons, state.SmallIcons })
            {
                if (icons.Remove(from, out var point))
                {
                    icons[to] = point;
                }
            }

            try
            {
                state.Save(folder);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                _log.Warn(error, $"finder: cannot save the window for {folder}");
            }
        }

        if (folder == DesktopFolder && _desktop?.Icons.FirstOrDefault(i => i.Key == from) is { } icon)
        {
            SetDesktopPosition(to, icon.Position);
        }
    }

    private void SetDesktopPosition(string name, Point position)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StateFile)!);
            var document = File.Exists(StateFile) ? TomlDocument.Load(StateFile) : TomlDocument.Parse("");
            document.Set("positions", name, TomlValue.Array([TomlValue.From((long)position.X), TomlValue.From((long)position.Y)]));
            document.Save(StateFile);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _log.Warn(error, $"finder: cannot save the desktop to {StateFile}");
        }
    }

    private void CleanUp()
    {
        if (ActiveFinderView is { IsSpatial: true } view)
        {
            view.CleanUp();
            BasinReport.Line($"CLEANUP {view.Folder}");
            return;
        }

        CleanUpDesktop();
    }

    internal IReadOnlyList<(string Path, string Label)> FolderChain(string folder)
    {
        if (folder == Trash.FilesDirectory)
        {
            return [(folder, "Trash")];
        }

        var chain = new List<(string Path, string Label)>();
        var root = Path.GetFullPath(_config.DiskRoot).TrimEnd(Path.DirectorySeparatorChar);
        for (var path = folder; path is not null; path = Path.GetDirectoryName(path))
        {
            var atRoot = path == root || (root.Length == 0 && path == "/");
            chain.Add((path, atRoot ? _config.DiskLabel : path == "/" ? "/" : Path.GetFileName(path)));
            if (atRoot)
            {
                break;
            }
        }

        return chain;
    }

    private bool PreparePathMenu(ShellWindow window, double titleX)
    {
        if (!window.IsShell || !window.ShellKey.StartsWith("finder:", StringComparison.Ordinal) ||
            window.ShellRoot is not { } root || window.ShellFrame is not { } frame)
        {
            return false;
        }

        var chain = FolderChain(window.ShellKey["finder:".Length..]);
        if (chain.Count < 2)
        {
            return false;
        }

        var folder = System7IconFamily.Generic("folder")!;
        var disk = System7IconFamily.Generic("hard-disk") ?? folder;
        var box = new ComboBox { Opacity = 0, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        for (var i = 0; i < chain.Count; i++)
        {
            var family = i == chain.Count - 1 && chain[i].Label == _config.DiskLabel ? disk : folder;
            var label = new TextBlock { Text = chain[i].Label, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            var icon = new Avalonia.Controls.Shapes.Path { Width = 16, Height = 16, Margin = new Avalonia.Thickness(0, -1, 0, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Data = (family.Small ?? family).Monochrome.InkGeometry };
            icon.Bind(Avalonia.Controls.Shapes.Shape.FillProperty, label.GetObservable(TextBlock.ForegroundProperty));
            box.Items.Add(new ComboBoxItem
            {
                Content = new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 4,
                    Height = 16,
                    Children = { icon, label },
                },
            });
        }

        box.SelectedIndex = 0;
        System7Theme.SetUseNativePopupTracking(box, true);
        GrowShellWindow(window, true);
        var titleWidth = System7Font.Measure(window.ShellTitle);
        box.Width = titleWidth + 48;
        box.Height = 17;
        var left = Math.Clamp((frame.Width - box.Width) / 2, titleX - box.Width + 4, titleX - 4);
        box.Margin = new Avalonia.Thickness(frame.Margin.Left + left, frame.Margin.Top + 1, 0, 0);
        root.Children.Add(box);
        root.UpdateLayout();
        _seat.Refresh();
        box.DropDownClosed += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var chosen = box.SelectedIndex;
            root.Children.Remove(box);
            GrowShellWindow(window, false);
            _outputs.ScheduleAll();
            if (chosen > 0)
            {
                BasinReport.Line($"PATHMENU {chain[chosen].Path}");
                OpenFinder(chain[chosen].Path, chain[chosen].Path == Trash.FilesDirectory ? "Trash" : chain[chosen].Label);
            }
        }, Avalonia.Threading.DispatcherPriority.Background);
        EventHandler<Avalonia.Input.PointerReleasedEventArgs>? released = null;
        released = (_, _) =>
        {
            root.RemoveHandler(Avalonia.Input.InputElement.PointerReleasedEvent, released!);
            if (!box.IsDropDownOpen && root.Children.Contains(box))
            {
                root.Children.Remove(box);
                GrowShellWindow(window, false);
            }
        };
        EventHandler<Avalonia.Input.PointerPressedEventArgs>? pressed = null;
        pressed = (_, e) =>
        {
            root.RemoveHandler(Avalonia.Input.InputElement.PointerPressedEvent, pressed!);
            if (box.IsDropDownOpen || TopLevel.GetTopLevel(root) is not { } top)
            {
                return;
            }

            e.Handled = true;
            box.RaiseEvent(new Avalonia.Input.PointerPressedEventArgs(box, e.Pointer, top, e.GetPosition(top), e.Timestamp,
                e.GetCurrentPoint(top).Properties, e.KeyModifiers, e.ClickCount));
        };
        root.AddHandler(Avalonia.Input.InputElement.PointerPressedEvent, pressed, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        root.AddHandler(Avalonia.Input.InputElement.PointerReleasedEvent, released, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        BasinReport.Line($"PATHMENU open {chain.Count}");
        _outputs.ScheduleAll();
        return true;
    }

    private void FillFinderView(FinderView view)
    {
        var entries = new List<FinderEntry>();
        try
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(view.Folder))
            {
                if (!Path.GetFileName(path).StartsWith('.') && FinderEntry.Read(path, _mime) is { } entry)
                {
                    entries.Add(entry);
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _log.Warn(error, $"finder: cannot read {view.Folder}");
        }

        view.SetEntries(entries);
        var free = "";
        try
        {
            var drive = new DriveInfo(view.Folder);
            free = $"   {drive.AvailableFreeSpace / 1024 / 1024:N0}MB available";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _log.Debug(error, $"finder: no free space for {view.Folder}");
        }

        view.SetHeader($"{entries.Count} item{(entries.Count == 1 ? "" : "s")}{free}");
    }

    private FinderView? ActiveFinderView =>
        _active is { IsShell: true } window && window.ShellKey.StartsWith("finder:", StringComparison.Ordinal)
            ? _finderViews.GetValueOrDefault(window.ShellKey["finder:".Length..])
            : null;

    private IReadOnlyList<string> SelectedPaths() =>
        ActiveFinderView is { } view
            ? view.Selection.Select(e => e.Path).ToList()
            : _desktop?.Selection.Where(i => i.Kind is DesktopIconKind.File or DesktopIconKind.Folder).Select(i => i.Path).ToList() ?? [];

    private void ApplyFinderView(string mode)
    {
        if (ActiveFinderView is { } view)
        {
            view.SetMode(mode);
            SaveFinderState(view.Folder);
        }
    }

    private void NewFolder()
    {
        var parent = ActiveFinderView?.Folder ?? DesktopFolder;
        var path = Path.Combine(parent, "untitled folder");
        for (var n = 2; Directory.Exists(path) || File.Exists(path); n++)
        {
            path = Path.Combine(parent, $"untitled folder {n}");
        }

        try
        {
            Directory.CreateDirectory(path);
            BasinReport.Line($"NEWFOLDER {path}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            FileError("The folder could not be created", error);
        }
    }

    private void OpenSelection()
    {
        if (ActiveFinderView is { } view)
        {
            foreach (var entry in view.Selection)
            {
                if (entry.IsDirectory)
                {
                    OpenFinder(entry.Path);
                }
                else
                {
                    OpenPath(entry.Path);
                }
            }

            return;
        }

        foreach (var icon in _desktop?.Selection.ToList() ?? [])
        {
            OpenIcon(icon);
        }
    }

    private void CloseFinderWindow()
    {
        if (_active is { IsShell: true } window)
        {
            CloseShellWindow(window);
        }
    }

    private void Duplicate()
    {
        foreach (var path in SelectedPaths())
        {
            try
            {
                var target = UniqueName(Path.GetDirectoryName(path)!, Path.GetFileName(path) + " copy");
                CopyPath(path, target);
                BasinReport.Line($"DUPLICATE {target}");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                FileError("The item could not be duplicated", error);
            }
        }
    }

    private void PutAway()
    {
        var items = Trash.Items();
        foreach (var path in SelectedPaths())
        {
            if (items.FirstOrDefault(i => i.FilesPath == path) is not { } item)
            {
                continue;
            }

            try
            {
                Trash.Restore(item);
                BasinReport.Line($"PUTAWAY {item.OriginalPath}");
            }
            catch (IOException error)
            {
                FileError("The item could not be put away", error);
            }
        }

        RefreshTrash();
    }

    private void SelectAll()
    {
        if (ActiveFinderView is { } view)
        {
            view.SelectAll();
        }
        else
        {
            _desktop?.SelectAll();
        }
    }

    private void CleanUpDesktop()
    {
        if (_desktop is not { } desktop || _desktopSurface is null)
        {
            return;
        }

        var width = _desktopSurface.DipWidth;
        var height = _desktopSurface.DipHeight;
        var rows = Math.Max(1, (height - 120) / 64);
        var slot = 0;
        foreach (var icon in desktop.Icons)
        {
            icon.Position = icon.Kind switch
            {
                DesktopIconKind.Trash => new Point(width - 80, height - 60),
                _ => new Point(width - 80 - (slot / rows * 72), 28 + (slot % rows * 64)),
            };
            if (icon.Kind != DesktopIconKind.Trash)
            {
                slot++;
            }
        }

        desktop.Refresh();
        SaveDesktopPositions();
    }

    private void RefreshTrash()
    {
        LoadDesktopIcons();
        BuildFinderMenus();
        _outputs.ScheduleAll();
    }

    private void Rename(string path, string name)
    {
        var target = Path.Combine(Path.GetDirectoryName(path)!, name);
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Move(path, target);
            }
            else
            {
                File.Move(path, target);
            }

            CarryPosition(Path.GetDirectoryName(path)!, Path.GetFileName(path), name);
            BasinReport.Line($"RENAME {target}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            FileError("The item could not be renamed", error);
        }
    }

    private void BeginFileDrag(IReadOnlyList<string> paths, bool copy, IReadOnlyList<(string Name, Vector Offset)> grab)
    {
        _fileDrag = (paths, copy, grab);
        _seat.Grabbing = true;
        BasinReport.Line($"FILEDRAG {paths.Count} {(copy ? "copy" : "move")}");
    }

    private void ShowFileOutline(double x, double y)
    {
        var view = _outputs.Views.FirstOrDefault(v => _layout.BoxOf(v.Output) is var box && x >= box.X && y >= box.Y && x < box.Right && y < box.Bottom);
        if (view is null || _outline is null)
        {
            return;
        }

        var box = _layout.BoxOf(view.Output);
        var scale = view.Output.Scale;
        var n = DevicePlacement.ChromeScale(scale);
        var px = (int)Math.Floor((x - box.X) * scale);
        var py = (int)Math.Floor((y - box.Y) * scale);
        var mode = view.Output.CurrentMode;
        _outline.Show(box.X, box.Y, scale, mode.Width, mode.Height, new Box(px - (16 * n), py - (16 * n), 32 * n, 32 * n), 0);
        _outputs.ScheduleAll();
    }

    private bool FinishFileDrag(double x, double y)
    {
        if (_fileDrag is not { } drag)
        {
            return false;
        }

        _fileDrag = null;
        _seat.Grabbing = false;
        _outline?.Hide();
        if (!drag.Copy && FinderDropAt(x, y) is ({ IsSpatial: true } finder, var local) &&
            drag.Paths.All(p => Path.GetDirectoryName(p) == finder.Folder) &&
            (finder.FolderAt(local) is not { } target || drag.Paths.Contains(target.Path)))
        {
            finder.MoveSelection(local - finder.DragOrigin);
            BasinReport.Line($"ARRANGED {finder.Folder} {drag.Paths.Count}");
            return true;
        }

        var folder = DropFolderAt(x, y);
        if (folder is not null)
        {
            var moved = Transfer(drag.Paths, folder, drag.Copy);
            PlaceDropped(moved, folder, x, y, drag.Grab);
        }

        return true;
    }

    private (FinderView View, Point Local)? FinderDropAt(double x, double y)
    {
        if (WindowAt(x, y, out var view, out var dx, out var dy) is { IsShell: true } window && view is not null &&
            window.ShellKey.StartsWith("finder:", StringComparison.Ordinal) &&
            _finderViews.GetValueOrDefault(window.ShellKey["finder:".Length..]) is { } finder)
        {
            var n = DevicePlacement.ChromeScale(view.Output.Scale);
            var content = window.ShellDevice;
            return (finder, new Point((dx - content.X) / (double)n, (dy - content.Y) / (double)n));
        }

        return null;
    }

    private string? DropFolderAt(double x, double y)
    {
        if (WindowAt(x, y, out var view, out _, out _) is not null && view is not null)
        {
            return FinderDropAt(x, y) is ({ } finder, var local) ? finder.FolderAt(local)?.Path ?? finder.Folder : null;
        }

        if (_desktop is { } desktop && MainView is { } main)
        {
            var box = _layout.BoxOf(main.Output);
            var n = DevicePlacement.ChromeScale(main.Output.Scale);
            var point = new Point((x - box.X) * main.Output.Scale / n, (y - box.Y) * main.Output.Scale / n);
            return desktop.IconAt(point) switch
            {
                { Kind: DesktopIconKind.Trash } => ":trash",
                { Kind: DesktopIconKind.Folder or DesktopIconKind.Disk } icon => icon.Path,
                _ => DesktopFolder,
            };
        }

        return null;
    }

    private void OnDesktopDrop(IReadOnlyList<DesktopIcon> icons, DesktopIcon? target, Point local, bool copy)
    {
        var paths = icons.Where(i => i.Kind is DesktopIconKind.File or DesktopIconKind.Folder).Select(i => i.Path).ToList();
        if (paths.Count == 0)
        {
            return;
        }

        string? folder = target switch
        {
            { Kind: DesktopIconKind.Trash } => ":trash",
            { Kind: DesktopIconKind.Folder or DesktopIconKind.Disk } => target.Path,
            _ => null,
        };
        if (folder is null)
        {
            var scene = DesktopToScene(local);
            folder = DropFolderAt(scene.X, scene.Y);
        }

        if (folder is not null)
        {
            var scene = DesktopToScene(local);
            var moved = Transfer(paths, folder, copy);
            PlaceDropped(moved, folder, scene.X, scene.Y, []);
        }
    }

    private void PlaceDropped(IReadOnlyList<(string From, string To)> moved, string folder, double x, double y, IReadOnlyList<(string Name, Vector Offset)> grab)
    {
        if (moved.Count == 0)
        {
            return;
        }

        if (FinderDropAt(x, y) is ({ IsSpatial: true } finder, var local) && finder.Folder == folder)
        {
            var at = finder.PanelPoint(local);
            finder.PlaceNames(moved.Select((m, i) =>
            {
                var offset = grab.FirstOrDefault(g => g.Name == Path.GetFileName(m.From)).Offset;
                return (Path.GetFileName(m.To), grab.Count > 0 ? at + offset : at + new Vector(-56 + (i * 16), -24 + (i * 16)));
            }));
            return;
        }

        if (folder == DesktopFolder && MainView is { } main && WindowAt(x, y, out _, out _, out _) is null)
        {
            var box = _layout.BoxOf(main.Output);
            var n = DevicePlacement.ChromeScale(main.Output.Scale);
            var point = new Point((x - box.X) * main.Output.Scale / n, (y - box.Y) * main.Output.Scale / n);
            for (var i = 0; i < moved.Count; i++)
            {
                SetDesktopPosition(Path.GetFileName(moved[i].To), new Point(point.X - 32 + (i * 16), point.Y - 16 + (i * 16)));
            }

            LoadDesktopIcons();
        }
    }

    internal List<(string From, string To)> Transfer(IReadOnlyList<string> paths, string folder, bool copy)
    {
        var moved = new List<(string From, string To)>();
        foreach (var path in paths)
        {
            try
            {
                if (folder == ":trash")
                {
                    Trash.Trash(path);
                    BasinReport.Line($"TRASHED {path}");
                    continue;
                }

                var target = Path.Combine(folder, Path.GetFileName(path));
                if (target == path || folder.StartsWith(path + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                {
                    continue;
                }

                if (File.Exists(target) || Directory.Exists(target))
                {
                    target = UniqueName(folder, Path.GetFileName(path));
                }

                if (copy)
                {
                    CopyPath(path, target);
                }
                else if (Directory.Exists(path))
                {
                    Directory.Move(path, target);
                }
                else
                {
                    File.Move(path, target);
                }

                moved.Add((path, target));
                BasinReport.Line($"{(copy ? "COPIED" : "MOVED")} {path} {target}");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                FileError($"“{Path.GetFileName(path)}” could not be {(copy ? "copied" : "moved")}", error);
            }
        }

        RefreshTrash();
        return moved;
    }

    private static string UniqueName(string folder, string name)
    {
        var stem = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        var target = Path.Combine(folder, name);
        for (var n = 2; File.Exists(target) || Directory.Exists(target); n++)
        {
            target = Path.Combine(folder, $"{stem} {n}{extension}");
        }

        return target;
    }

    private static void CopyPath(string from, string to)
    {
        if (File.Exists(from))
        {
            File.Copy(from, to);
            return;
        }

        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from))
        {
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        }

        foreach (var directory in Directory.EnumerateDirectories(from))
        {
            CopyPath(directory, Path.Combine(to, Path.GetFileName(directory)));
        }
    }

    private void FileError(string what, Exception error)
    {
        _log.Warn(error, $"finder: {char.ToLowerInvariant(what[0])}{what[1..]}");
        ShowAlert("Finder", FileErrorText(what, error), ["OK"], _ => { }, icon: "stop");
    }

    internal static string FileErrorText(string what, Exception error) => error switch
    {
        UnauthorizedAccessException => $"{what}. You don't have permission to change it.",
        IOException { HResult: 28 or 122 } => $"{what}. The disk is full.",
        IOException { HResult: 30 } => $"{what}. The disk is locked.",
        _ => $"{what}.",
    };
}
