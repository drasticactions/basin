using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace LunaWm;

public sealed partial class LauncherView : UserControl
{
    public const double TapSlop = 10;
    public static readonly TimeSpan HoldTime = TimeSpan.FromMilliseconds(500);

    private readonly DispatcherTimer _hold = new() { Interval = HoldTime };
    private LauncherIconModel? _pressed;
    private Point _start;
    private bool _moved;
    private bool _dragging;

    public LauncherView()
    {
        InitializeComponent();
        _hold.Tick += (_, _) => BeginDrag();
    }

    private LauncherModel? Model => DataContext as LauncherModel;

    private void OnScrollerTemplate(object? sender, TemplateAppliedEventArgs e)
    {
        foreach (var name in new[] { "PART_FadeTop", "PART_FadeBottom" })
        {
            if (e.NameScope.Find<Border>(name) is { } fade)
            {
                fade.IsVisible = true;
            }
        }
    }

    private void OnTab(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Control { Tag: LauncherTabModel tab } && Model is { } model)
        {
            model.Current = tab;
        }
    }

    private void OnDone(object? sender, PointerReleasedEventArgs e)
    {
        if (Model is { } model)
        {
            model.Reordering = false;
        }
    }

    private void OnSearch(object? sender, PointerReleasedEventArgs e) => Model?.Search();

    private void OnHide(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Control { Tag: LauncherIconModel icon } && Model is { Reordering: true } model)
        {
            e.Handled = true;
            model.Hide(icon);
        }
    }

    private void OnIconPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { Tag: LauncherIconModel icon } control)
        {
            return;
        }

        _pressed = icon;
        _start = e.GetPosition(this);
        _moved = false;
        _dragging = false;
        e.Pointer.Capture(control);
        _hold.Stop();
        _hold.Start();
        if (Model is { Reordering: true })
        {
            BeginDrag();
        }
    }

    private void OnIconMoved(object? sender, PointerEventArgs e)
    {
        if (_pressed is not { } icon || Model is not { } model)
        {
            return;
        }

        var point = e.GetPosition(this);
        if (!_moved && Distance(point, _start) > TapSlop)
        {
            _moved = true;
            if (!_dragging)
            {
                _hold.Stop();
                _pressed = null;
                e.Pointer.Capture(null);
                return;
            }
        }

        if (!_dragging)
        {
            return;
        }

        Canvas.SetLeft(DragImage, point.X - 36);
        Canvas.SetTop(DragImage, point.Y - 36);
        model.DropSlot = DockSlotAt(point);
        if (TabAt(point) is { } tab)
        {
            model.MoveToTab(icon, tab);
            model.Current = tab;
            return;
        }

        var index = IndexAt(point);
        if (index >= 0)
        {
            model.MoveWithin(icon, index);
        }
    }

    private void OnIconReleased(object? sender, PointerReleasedEventArgs e)
    {
        _hold.Stop();
        if (_pressed is not { } icon || Model is not { } model)
        {
            return;
        }

        _pressed = null;
        e.Pointer.Capture(null);
        if (_dragging)
        {
            var slot = DockSlotAt(e.GetPosition(this));
            EndDrag(icon);
            if (slot >= 0)
            {
                model.DockAt(slot, icon);
            }

            return;
        }

        if (!_moved)
        {
            model.Launch(icon);
        }
    }

    private void OnIconLost(object? sender, PointerCaptureLostEventArgs e)
    {
        _hold.Stop();
        if (_dragging && _pressed is { } icon)
        {
            EndDrag(icon);
        }

        _pressed = null;
    }

    private void BeginDrag()
    {
        _hold.Stop();
        if (_pressed is not { } icon || Model is not { } model || _moved && !model.Reordering)
        {
            return;
        }

        model.Reordering = true;
        _dragging = true;
        icon.Dragging = true;
        DragImage.Source = icon.Icon as Avalonia.Media.IImage;
        DragImage.IsVisible = icon.Icon is not null;
        Canvas.SetLeft(DragImage, _start.X - 36);
        Canvas.SetTop(DragImage, _start.Y - 36);
    }

    private void EndDrag(LauncherIconModel icon)
    {
        _dragging = false;
        icon.Dragging = false;
        DragImage.IsVisible = false;
        if (Model is { } model)
        {
            model.DropSlot = -1;
        }
    }

    private int IndexAt(Point point)
    {
        var grid = this.FindControl<ItemsControl>("Grid")!;
        for (var i = 0; i < grid.ItemCount; i++)
        {
            if (grid.ContainerFromIndex(i) is { } container && Contains(container, point))
            {
                return i;
            }
        }

        return -1;
    }

    private LauncherTabModel? TabAt(Point point)
    {
        var bar = this.FindControl<ItemsControl>("TabBar")!;
        for (var i = 0; i < bar.ItemCount; i++)
        {
            if (bar.ContainerFromIndex(i) is { } container && Contains(container, point) && Model?.Tabs[i] is { } tab)
            {
                return tab;
            }
        }

        return null;
    }

    private int DockSlotAt(Point point)
    {
        var dock = this.FindControl<ItemsControl>("DockItems")!;
        var row = this.FindControl<Control>("DockRow")!;
        if (!row.IsVisible || !Contains(row, point))
        {
            return -1;
        }

        for (var i = 0; i < dock.ItemCount; i++)
        {
            if (dock.ContainerFromIndex(i) is { } container && Contains(container, point))
            {
                return i;
            }
        }

        return dock.ItemCount < 6 ? dock.ItemCount : -1;
    }

    private bool Contains(Visual visual, Point point)
    {
        if (visual.TranslatePoint(default, this) is not { } origin)
        {
            return false;
        }

        return new Rect(origin, visual.Bounds.Size).Contains(point);
    }

    private static double Distance(Point a, Point b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

    private void OnTabInvoked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: LauncherTabModel tab } && Model is { } model)
        {
            model.Current = tab;
        }
    }

    private void OnDoneInvoked(object? sender, RoutedEventArgs e)
    {
        if (Model is { } model)
        {
            model.Reordering = false;
        }
    }

    private void OnSearchInvoked(object? sender, RoutedEventArgs e) => Model?.Search();

    private void OnIconInvoked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: LauncherIconModel icon } && Model is { Reordering: false } model)
        {
            model.Launch(icon);
        }
    }

    private void OnHideInvoked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: LauncherIconModel icon } && Model is { Reordering: true } model)
        {
            e.Handled = true;
            model.Hide(icon);
        }
    }
}
