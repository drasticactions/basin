using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Basin.Portal.Prompts.Avalonia;

public sealed partial class ShortcutPromptView : UserControl
{
    public ShortcutPromptView()
    {
        AvaloniaXamlLoader.Load(this);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AttachedToVisualTree += (_, _) => Focus();
    }

    private void OnRowClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ShortcutPromptModel model && sender is Button { Tag: ShortcutRowModel row })
        {
            model.ToggleCapture(row);
            Focus();
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ShortcutPromptModel model)
        {
            return;
        }

        if (model.Capturing is not null)
        {
            if (e.Key == Key.Escape)
            {
                model.ToggleCapture(model.Capturing);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Back)
            {
                model.Clear();
                e.Handled = true;
                return;
            }

            if (AvaloniaKeys.IsModifier(e.Key))
            {
                return;
            }

            if (model.Capture(AvaloniaKeys.KeysymNameOf(e.Key), AvaloniaKeys.ModifiersOf(e.KeyModifiers)))
            {
                e.Handled = true;
            }

            return;
        }

        if (e.Key == Key.Escape)
        {
            model.Cancel.Execute(null);
            e.Handled = true;
        }
        else if (e.Key is Key.Enter or Key.Return)
        {
            model.Accept.Execute(null);
            e.Handled = true;
        }
    }
}
