using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Basin.Portal.Prompts.Avalonia;

public sealed partial class SourcePromptView : UserControl
{
    private int _cursor = -1;

    public SourcePromptView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AttachedToVisualTree += (_, _) => Focus();
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not SourcePromptModel model)
        {
            return;
        }

        foreach (var removed in e.RemovedItems)
        {
            if (removed is SourceRowModel row)
            {
                model.SetSelected(row, false);
            }
        }

        foreach (var added in e.AddedItems)
        {
            if (added is SourceRowModel row)
            {
                model.SetSelected(row, true);
            }
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not SourcePromptModel model)
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
        else if (e.Key is Key.Down or Key.Up)
        {
            MoveCursor(model, e.Key == Key.Down ? 1 : -1);
            e.Handled = true;
        }
        else if (e.Key == Key.Space && model.Multiple && _cursor >= 0 && _cursor < model.Rows.Count)
        {
            var row = model.Rows[_cursor];
            if (model.IsSelected(row))
            {
                Rows.SelectedItems!.Remove(row);
            }
            else
            {
                Rows.SelectedItems!.Add(row);
            }

            e.Handled = true;
        }
    }

    private void MoveCursor(SourcePromptModel model, int step)
    {
        if (model.Rows.Count == 0)
        {
            return;
        }

        _cursor = Math.Clamp(_cursor + step, 0, model.Rows.Count - 1);
        if (model.Multiple)
        {
            Rows.ContainerFromIndex(_cursor)?.Focus();
            return;
        }

        Rows.SelectedIndex = _cursor;
    }
}
