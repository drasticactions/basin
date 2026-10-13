using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace LunaWm;

public sealed partial class SystemMenuView : UserControl
{
    public SystemMenuView() => InitializeComponent();

    private SystemMenuModel? Model => DataContext as SystemMenuModel;

    private void OnBackground(object? sender, PointerPressedEventArgs e) => Model?.Dismiss();

    private void OnPanel(object? sender, PointerPressedEventArgs e) => e.Handled = true;

    private void OnToggle(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Control { Tag: ToggleRow row })
        {
            row.Toggle();
            Model?.Choose(row);
        }
    }

    private void OnPassphraseKey(object? sender, KeyEventArgs e)
    {
        if (sender is Control { Tag: PassphraseRow row })
        {
            if (e.Key == Key.Enter)
            {
                row.Submit();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                row.Cancelled?.Invoke();
                e.Handled = true;
            }
        }
    }

    private void OnPassphraseShown(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (sender is TextBox box)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => box.Focus(NavigationMethod.Tab), Avalonia.Threading.DispatcherPriority.Background);
        }
    }

    private void OnPassphraseJoin(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Control { Tag: PassphraseRow row })
        {
            row.Submit();
        }
    }

    private void OnPassphraseCancel(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Control { Tag: PassphraseRow row })
        {
            row.Cancelled?.Invoke();
        }
    }

    private void OnAction(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Control { Tag: ActionRow row, IsEffectivelyEnabled: true })
        {
            row.Invoked?.Invoke();
            Model?.Choose(row);
        }
    }
}
