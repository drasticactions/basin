using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace LunaWm;

public sealed partial class JustTypeView : UserControl
{
    public JustTypeView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (Model is { } model)
            {
                model.QueryChanged += _ => Dispatcher.UIThread.Post(MoveCaretToEnd, DispatcherPriority.Background);
            }
        };
    }

    private JustTypeModel? Model => DataContext as JustTypeModel;

    public void FocusField()
    {
        Field.Focus();
        MoveCaretToEnd();
    }

    private void MoveCaretToEnd()
    {
        Field.CaretIndex = Field.Text?.Length ?? 0;
        Field.ClearSelection();
    }

    private void OnFieldKey(object? sender, KeyEventArgs e)
    {
        if (Model is not { } model)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Down:
                model.Move(1);
                e.Handled = true;
                break;
            case Key.Up:
                model.Move(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                model.ChooseSelected();
                e.Handled = true;
                break;
            case Key.Escape:
                model.Dismiss();
                e.Handled = true;
                break;
        }
    }

    private void OnRow(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Control { Tag: JustTypeRow row })
        {
            Model?.Choose(row);
        }
    }

    private void OnRowInvoked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: JustTypeRow row })
        {
            Model?.Choose(row);
        }
    }
}
