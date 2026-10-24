using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Basin;
using Basin.AppMenu;
using Basin.Scene;
using Basin.UI.Avalonia;
using System7.Avalonia;
using System7.Avalonia.Controls;
using System7.Avalonia.Rendering;

namespace System7Wm;

internal sealed class MenuBar : IDisposable
{
    public const int Height = 20;

    private readonly ChromeSurface _surface;
    private readonly Menu _menu = new() { VerticalAlignment = VerticalAlignment.Top };
    private readonly Panel _root = new() { Background = null };
    private readonly List<(MenuItem Item, AppMenuItem Model, IAppMenuSource Source)> _titles = [];
    private IAppMenuSource? _app;
    private bool _dirty;
    private bool _grown;
    private int _originX;
    private int _originY;
    private double _scale = 1;
    private Box _output;
    private (int Width, int Height) _device;

    public MenuBar(SceneTree layer, AvaloniaUIHost host, UISurfaceIndex index)
    {
        System7Theme.SetUseNativeMenuTracking(_menu, true);
        System7Theme.SetUseMenuFlash(_menu, true);
        _root.Children.Add(_menu);
        _surface = new ChromeSurface(layer, host, index, () => _root, "Menu bar");
        _menu.Opened += (_, _) => Opened?.Invoke();
        _menu.Closed += (_, _) => Dispatcher.UIThread.Post(OnClosed, DispatcherPriority.Background);
    }

    public LocalAppMenu Apple { get; } = new();

    public LocalAppMenu Application { get; } = new();

    public IAppMenuSource? App
    {
        get => _app;
        set
        {
            if (ReferenceEquals(_app, value))
            {
                return;
            }

            if (_app is not null)
            {
                _app.Menu.Changed -= OnModelChanged;
            }

            _app = value;
            if (_app is not null)
            {
                _app.Menu.Changed += OnModelChanged;
            }

            Rebuild();
        }
    }

    public Func<Control>? ApplicationTitle { get; set; }

    public string? Clock { get; private set; }

    public System7ColorDepth Depth
    {
        get => System7Theme.GetColorDepth(_root);
        set => System7Theme.SetColorDepth(_root, value);
    }

    public ChromeSurface Surface => _surface;

    public bool IsOpen => _menu.IsOpen;

    public bool IsGrown => _grown;

    public event Action? Opened;

    public event Action? Shrunk;

    public event Action? Resized;

    public void Place(int originX, int originY, double scale, in Box output, (int Width, int Height) device)
    {
        _device = device;
        _originX = originX;
        _originY = originY;
        _scale = scale;
        _output = output;
        Apply();
    }

    public void Grow()
    {
        if (_grown)
        {
            return;
        }

        _grown = true;
        Apply();
        _root.Measure(new Size(_surface.DipWidth, _surface.DipHeight));
        _root.Arrange(new Rect(0, 0, _surface.DipWidth, _surface.DipHeight));
        Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
    }

    public void SetClock(string? text)
    {
        if (Clock == text)
        {
            return;
        }

        Clock = text;
        Rebuild();
    }

    public void Rebuild()
    {
        if (_menu.IsOpen)
        {
            _dirty = true;
            return;
        }

        _dirty = false;
        _titles.Clear();
        _menu.Items.Clear();
        var apple = Apple.Menu.Items.FirstOrDefault();
        if (apple is not null)
        {
            var item = Build(apple, Apple);
            item.Header = "\u0014";
            global::Avalonia.Automation.AutomationProperties.SetName(item, "Apple");
            System7Theme.SetIsAppleMenu(item, true);
            Add(item, apple, Apple);
        }

        if (_app is { } app)
        {
            foreach (var title in app.Menu.Items)
            {
                if (title.Visible)
                {
                    Add(Build(title, app), title, app);
                }
            }
        }

        if (Clock is { } clock)
        {
            var item = new MenuItem { Header = clock };
            System7Theme.SetMenuBarDock(item, HorizontalAlignment.Right);
            _menu.Items.Add(item);
        }

        if (Application.Menu.Items.FirstOrDefault() is { } application)
        {
            var item = Build(application, Application);
            if (ApplicationTitle is { } icon)
            {
                item.Header = icon();
                global::Avalonia.Automation.AutomationProperties.SetName(item, "Application");
            }

            System7Theme.SetMenuBarDock(item, HorizontalAlignment.Right);
            Add(item, application, Application);
        }
    }

    public IReadOnlyList<(string Label, AppMenuItem Model, IAppMenuSource Source)> Titles =>
        _titles.Select(t => (t.Model.Label, t.Model, t.Source)).ToList();

    public bool TryShortcut(string keysym, uint timestamp)
    {
        foreach (var (title, model, source) in _titles)
        {
            if (Find(model, keysym) is { } match)
            {
                Flash(title);
                source.Activate(match, timestamp);
                return true;
            }
        }

        return false;
    }

    public bool Choose(string path, uint timestamp)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        foreach (var (title, model, source) in _titles)
        {
            if (!Matches(model, parts[0], title))
            {
                continue;
            }

            if (parts.Length == 1)
            {
                Grow();
                _menu.Open();
                title.IsSubMenuOpen = true;
                return true;
            }

            source.AboutToShow(model);
            var current = model;
            for (var i = 1; i < parts.Length; i++)
            {
                current = current.Children.FirstOrDefault(c => string.Equals(c.Label, parts[i], StringComparison.OrdinalIgnoreCase));
                if (current is null)
                {
                    return false;
                }

                if (i < parts.Length - 1)
                {
                    source.AboutToShow(current);
                }
            }

            if (!current.Enabled || current.IsSubmenu)
            {
                return false;
            }

            Flash(title);
            source.Activate(current, timestamp);
            return true;
        }

        return false;
    }

    public void Close()
    {
        if (_menu.IsOpen)
        {
            _menu.Close();
        }
    }

    public void Dispose() => _surface.Dispose();

    private static bool Matches(AppMenuItem model, string label, MenuItem title) =>
        string.Equals(model.Label, label, StringComparison.OrdinalIgnoreCase) ||
        (System7Theme.GetIsAppleMenu(title) && label is "apple" or "Apple" or "\u0014");

    private static AppMenuItem? Find(AppMenuItem item, string keysym)
    {
        foreach (var child in item.Children)
        {
            if (child.Enabled && child.Shortcut is { Modifiers: AppMenuModifiers.Control } shortcut && shortcut.Keysym == keysym)
            {
                return child;
            }

            if (child.IsSubmenu && Find(child, keysym) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    private static void Flash(MenuItem title)
    {
        title.IsSelected = true;
        DispatcherTimer.RunOnce(() => title.IsSelected = false, TimeSpan.FromMilliseconds(120));
    }

    private void Add(MenuItem item, AppMenuItem model, IAppMenuSource source)
    {
        _titles.Add((item, model, source));
        _menu.Items.Add(item);
    }

    internal static MenuItem Build(AppMenuItem model, IAppMenuSource source)
    {
        if (model.Kind == AppMenuItemKind.Separator)
        {
            return new MenuItem { Header = "-" };
        }

        var item = new MenuItem();
        Update(item, model);
        if (model.IsSubmenu)
        {
            Fill(item, model, source);
            item.SubmenuOpened += (_, e) =>
            {
                if (ReferenceEquals(e.Source, item))
                {
                    source.AboutToShow(model);
                }
            };
        }
        else
        {
            item.Click += (_, e) =>
            {
                if (ReferenceEquals(e.Source, item))
                {
                    source.Activate(model, (uint)Environment.TickCount);
                }
            };
        }

        return item;
    }

    private static void Fill(MenuItem item, AppMenuItem model, IAppMenuSource source)
    {
        item.Items.Clear();
        foreach (var child in model.Children)
        {
            if (child.Visible)
            {
                item.Items.Add(Build(child, source));
            }
        }

        if (item.Items.Count == 0)
        {
            item.Items.Add(new MenuItem { Header = " ", IsEnabled = false });
        }
    }

    private static void Update(MenuItem item, AppMenuItem model)
    {
        item.Header = model.Label;
        item.IsEnabled = model.Enabled;
        item.IsVisible = model.Visible;
        if (model.Kind is AppMenuItemKind.Check or AppMenuItemKind.Radio)
        {
            item.ToggleType = model.Kind == AppMenuItemKind.Check ? MenuItemToggleType.CheckBox : MenuItemToggleType.Radio;
            item.IsChecked = model.ToggleState;
        }

        item.InputGesture = GestureOf(model.Shortcut);
    }

    internal static KeyGesture? GestureOf(AppMenuShortcut? shortcut)
    {
        if (shortcut is not { Modifiers: AppMenuModifiers.Control, Keysym: { Length: 1 } key })
        {
            return null;
        }

        var c = char.ToUpperInvariant(key[0]);
        Key? mapped = c switch
        {
            >= 'A' and <= 'Z' => Key.A + (c - 'A'),
            >= '0' and <= '9' => Key.D0 + (c - '0'),
            _ => null,
        };
        return mapped is { } k ? new KeyGesture(k, KeyModifiers.Meta) : null;
    }

    private void OnModelChanged()
    {
        if (!_menu.IsOpen)
        {
            Rebuild();
            return;
        }

        _dirty = true;
        foreach (var (title, model, source) in _titles)
        {
            if (ReferenceEquals(source, _app))
            {
                Patch(title, model, source);
            }
        }
    }

    private void Patch(MenuItem item, AppMenuItem model, IAppMenuSource source)
    {
        if (!model.IsSubmenu)
        {
            return;
        }

        var shown = item.Items.OfType<MenuItem>().ToList();
        var visible = model.Children.Where(c => c.Visible).ToList();
        if (shown.Count != visible.Count || !item.IsSubMenuOpen)
        {
            if (!item.IsSubMenuOpen)
            {
                Fill(item, model, source);
            }

            return;
        }

        for (var i = 0; i < visible.Count; i++)
        {
            if (visible[i].Kind != AppMenuItemKind.Separator)
            {
                Update(shown[i], visible[i]);
                Patch(shown[i], visible[i], source);
            }
        }
    }

    private void OnClosed()
    {
        if (_menu.IsOpen)
        {
            return;
        }

        if (_grown)
        {
            _grown = false;
            Apply();
            Shrunk?.Invoke();
        }

        if (_dirty)
        {
            Rebuild();
        }
    }

    private void Apply()
    {
        if (_output.Width <= 0)
        {
            return;
        }

        var chrome = DevicePlacement.ChromeScale(_scale);
        var width = _device.Width;
        var height = _grown ? _device.Height : Height * chrome;
        _surface.Place(_originX, _originY, _scale, new Box(0, 0, width, height));
        _menu.Width = _surface.DipWidth;
        Resized?.Invoke();
    }
}
