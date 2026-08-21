using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Input;
using Avalonia.Reactive;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Basin.Avalonia;

internal static class TitleBarBehavior
{
    private static readonly ConditionalWeakTable<Visual, object> Adopted = [];
    private static IDisposable? _subscription;

    internal static void Install() =>
        _subscription ??= WindowDecorationProperties.ElementRoleProperty.Changed.Subscribe(
            new AnonymousObserver<AvaloniaPropertyChangedEventArgs<WindowDecorationsElementRole>>(OnRoleChanged));

    private static void OnRoleChanged(AvaloniaPropertyChangedEventArgs<WindowDecorationsElementRole> e)
    {
        if (e.NewValue.GetValueOrDefault() != WindowDecorationsElementRole.TitleBar ||
            e.Sender is not Visual titleBar)
        {
            return;
        }

        Dispatcher.UIThread.Post(() => Adopt(titleBar), DispatcherPriority.Loaded);
    }

    private static void Adopt(Visual titleBar)
    {
        if (WindowDecorationProperties.GetElementRole(titleBar) != WindowDecorationsElementRole.TitleBar)
        {
            return;
        }

        if (!titleBar.IsAttachedToVisualTree())
        {
            titleBar.AttachedToVisualTree += OnPendingAttached;
            return;
        }

        if (FindWindow(titleBar) is null)
        {
            return;
        }

        WindowDecorationProperties.SetElementRole(titleBar, WindowDecorationsElementRole.User);
        if (titleBar is InputElement input && Adopted.TryAdd(titleBar, titleBar))
        {
            input.AddHandler(InputElement.PointerPressedEvent, OnTitleBarPressed);
        }
    }

    private static void OnPendingAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Visual titleBar)
        {
            titleBar.AttachedToVisualTree -= OnPendingAttached;
            Adopt(titleBar);
        }
    }

    private static void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Visual titleBar ||
            FindWindow(titleBar) is not { } window ||
            window.WindowState == WindowState.FullScreen ||
            e.GetCurrentPoint(titleBar).Properties.PointerUpdateKind != PointerUpdateKind.LeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            if (window.CanResize)
            {
                window.WindowState = window.WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
            }

            e.Handled = true;
        }
        else
        {
            window.BeginMoveDrag(e);
        }
    }

    private static ToplevelWindow? FindWindow(Visual titleBar)
    {
        var root = titleBar;
        while (root.GetVisualParent() is { } parent)
        {
            root = parent;
        }

        if (root is ToplevelWindow window)
        {
            return window;
        }

        foreach (var descendant in root.GetVisualDescendants())
        {
            if (descendant is ToplevelWindow found)
            {
                return found;
            }
        }

        return null;
    }
}
