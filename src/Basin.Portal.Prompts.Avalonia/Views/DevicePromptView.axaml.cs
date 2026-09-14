using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace Basin.Portal.Prompts.Avalonia;

public sealed partial class DevicePromptView : UserControl
{
    public DevicePromptView()
    {
        AvaloniaXamlLoader.Load(this);
        KeyDown += OnKeyDown;
        AttachedToVisualTree += (_, _) => Focus();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not DevicePromptModel model)
        {
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
