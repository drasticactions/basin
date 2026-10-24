using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using AvaUnion.Controls;

namespace TabWm.Chrome;

public sealed partial class StripView : UserControl
{
    public const double DragThreshold = 4;

    public const double DragOutDistance = 24;

    private readonly TabControl _tabs;
    private TabModel? _pressed;
    private Point _pressPoint;
    private bool _dragging;

    public StripView()
    {
        AvaloniaXamlLoader.Load(this);
        _tabs = this.FindControl<TabControl>("Tabs")!;
        _tabs.ContainerPrepared += OnContainerPrepared;
        _tabs.ContainerClearing += OnContainerClearing;
        ClosableTabs.AddCloseRequestedHandler(_tabs, OnCloseRequested);
        LayoutUpdated += (_, _) => Measured();
    }

    private StripModel? Model => DataContext as StripModel;

    internal TabControl TabControl => _tabs;

    internal bool IsDragging => _dragging;

    private void Measured()
    {
        if (Model is not { } model || _tabs.DesiredSize.Height <= 0)
        {
            return;
        }

        model.PreferredHeight = Math.Ceiling(_tabs.DesiredSize.Height + 3);
    }

    private void OnContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (e.Container is not TabItem item)
        {
            return;
        }

        item.AddHandler(PointerPressedEvent, OnTabPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        item.AddHandler(PointerMovedEvent, OnTabMoved, RoutingStrategies.Bubble, handledEventsToo: true);
        item.AddHandler(PointerReleasedEvent, OnTabReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        item.AddHandler(PointerCaptureLostEvent, OnCaptureLost, RoutingStrategies.Bubble, handledEventsToo: true);
        item.DoubleTapped += OnTabDoubleTapped;
        item.ContextRequested += OnTabContextRequested;
        if (item.DataContext is TabModel model)
        {
            model.PropertyChanged += OnTabPropertyChanged;
        }
    }

    private void OnContainerClearing(object? sender, ContainerClearingEventArgs e)
    {
        if (e.Container is not TabItem item)
        {
            return;
        }

        item.RemoveHandler(PointerPressedEvent, OnTabPressed);
        item.RemoveHandler(PointerMovedEvent, OnTabMoved);
        item.RemoveHandler(PointerReleasedEvent, OnTabReleased);
        item.RemoveHandler(PointerCaptureLostEvent, OnCaptureLost);
        item.DoubleTapped -= OnTabDoubleTapped;
        item.ContextRequested -= OnTabContextRequested;
        if (item.DataContext is TabModel model)
        {
            model.PropertyChanged -= OnTabPropertyChanged;
        }
    }

    private void OnCloseRequested(object? sender, TabCloseRequestedEventArgs e)
    {
        if (e.Tab.DataContext is TabModel model)
        {
            Model?.CloseRequested?.Invoke(model);
        }

        e.Handled = true;
    }

    private void OnTabPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not TabItem { DataContext: TabModel model } || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || model.IsEditing)
        {
            return;
        }

        _pressed = model;
        _pressPoint = e.GetPosition(this);
        _dragging = false;
    }

    private void OnTabMoved(object? sender, PointerEventArgs e)
    {
        if (_pressed is not { } tab || Model is not { } model)
        {
            return;
        }

        var point = e.GetPosition(this);
        if (!_dragging)
        {
            if (Math.Abs(point.X - _pressPoint.X) < DragThreshold && Math.Abs(point.Y - _pressPoint.Y) < DragThreshold)
            {
                return;
            }

            _dragging = true;
        }

        if (point.Y < -DragOutDistance || point.Y > Bounds.Height + DragOutDistance)
        {
            if (model.DragOut?.Invoke(tab) == true)
            {
                _pressed = null;
                _dragging = false;
                e.Pointer.Capture(null);
            }

            return;
        }

        var target = IndexAt(point.X);
        if (target >= 0 && target != model.Tabs.IndexOf(tab))
        {
            model.Reordered?.Invoke(tab, target);
        }
    }

    private void OnTabReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Middle && sender is TabItem item)
        {
            item.RaiseEvent(new TabCloseRequestedEventArgs(item));
            e.Handled = true;
            return;
        }

        _pressed = null;
        _dragging = false;
    }

    private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _pressed = null;
        _dragging = false;
    }

    private void OnTabDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is TabItem { DataContext: TabModel tab })
        {
            Model?.BeginRename(tab);
            e.Handled = true;
        }
    }

    private void OnTabContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (sender is not TabItem { DataContext: TabModel tab } item || Model is not { } model)
        {
            return;
        }

        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "Rename Tab", Command = model.RenameCommand, CommandParameter = tab });
        menu.Items.Add(new MenuItem { Header = "Launch App…", Command = model.LaunchCommand, CommandParameter = tab });
        menu.Items.Add(new MenuItem
        {
            Header = tab.Audio == TabAudioState.Muted ? "Unmute Tab" : "Mute Tab",
            Command = model.MuteCommand,
            CommandParameter = tab,
        });
        if (tab.HasHidden)
        {
            var hidden = new MenuItem { Header = "Hidden Windows" };
            foreach (var window in tab.Hidden)
            {
                hidden.Items.Add(new MenuItem { Header = window.Title, Command = model.RestoreCommand, CommandParameter = window });
            }

            menu.Items.Add(hidden);
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "Close Tab", Command = model.CloseCommand, CommandParameter = tab });
        item.ContextMenu = menu;
        menu.Open(item);
        e.Handled = true;
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TabModel.IsEditing) || sender is not TabModel { IsEditing: true } tab)
        {
            return;
        }

        if (_tabs.ContainerFromItem(tab) is not { } container)
        {
            return;
        }

        foreach (var box in container.GetVisualDescendants().OfType<TextBox>())
        {
            if (!box.Classes.Contains("tab-rename"))
            {
                continue;
            }

            box.KeyDown -= OnRenameKey;
            box.KeyDown += OnRenameKey;
            box.LostFocus -= OnRenameLostFocus;
            box.LostFocus += OnRenameLostFocus;
            box.Focus();
            box.SelectAll();
        }
    }

    private void OnRenameKey(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: TabModel tab } || Model is not { } model)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            model.CommitRename(tab, keep: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            model.CommitRename(tab, keep: false);
            e.Handled = true;
        }
    }

    private void OnRenameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: TabModel tab } && Model is { } model)
        {
            model.CommitRename(tab, keep: true);
        }
    }

    public int IndexAtStrip(double x) => IndexAt(x);

    private int IndexAt(double x)
    {
        if (Model is not { } model)
        {
            return -1;
        }

        for (var i = 0; i < model.Tabs.Count; i++)
        {
            if (_tabs.ContainerFromIndex(i) is not Control container || container.TranslatePoint(default, this) is not { } origin)
            {
                continue;
            }

            if (x >= origin.X && x < origin.X + container.Bounds.Width)
            {
                return i;
            }
        }

        return -1;
    }
}
