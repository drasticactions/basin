using Avalonia.VisualTree;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using AvaEnyo.Controls;

namespace LunaWm;

public sealed partial class DashboardView : UserControl
{
    public DashboardView() => InitializeComponent();

    private DashboardModel? Model => DataContext as DashboardModel;

    private void OnBackground(object? sender, PointerPressedEventArgs e) => Model?.Dismiss();

    private void OnColumn(object? sender, PointerPressedEventArgs e) => e.Handled = true;

    private static bool Still(object? sender) =>
        (sender as Control)?.FindAncestorOfType<SwipeableItem>() is not { } item || Math.Abs(item.SwipeOffset) < 4;

    private void OnBadge(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Control { Tag: DashboardGroupModel group } && Still(sender))
        {
            e.Handled = true;
            group.Expanded = !group.Expanded;
        }
    }

    private void OnGroupTapped(object? sender, PointerReleasedEventArgs e)
    {
        if (!e.Handled && sender is Control { Tag: DashboardGroupModel group } && Still(sender))
        {
            Model?.Tap(group);
        }
    }

    private void OnGroupSwiped(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: DashboardGroupModel group })
        {
            Model?.Swipe(group);
        }
    }

    private void OnItemTapped(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Control { Tag: DashboardItemModel item } && Still(sender))
        {
            Model?.TapItem(item);
        }
    }

    private void OnItemSwiped(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: DashboardItemModel item })
        {
            Model?.SwipeItem(item);
        }
    }

    private void OnBadgeInvoked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: DashboardGroupModel group })
        {
            e.Handled = true;
            group.Expanded = !group.Expanded;
        }
    }

    private void OnGroupInvoked(object? sender, RoutedEventArgs e)
    {
        if (!e.Handled && sender is Control { Tag: DashboardGroupModel group })
        {
            Model?.Tap(group);
        }
    }

    private void OnItemInvoked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: DashboardItemModel item })
        {
            Model?.TapItem(item);
        }
    }
}
