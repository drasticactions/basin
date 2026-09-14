using Avalonia;
using AvaloniaPoint = Avalonia.Point;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;

namespace Basin.Portal.Prompts.Avalonia;

public sealed partial class AreaPromptView : UserControl
{
    private AvaloniaPoint _start;
    private bool _dragging;

    public AreaPromptView()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
        PointerPressed += OnPressed;
        PointerMoved += OnMoved;
        PointerReleased += OnReleased;
        AttachedToVisualTree += (_, _) => Focus();
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _start = e.GetPosition(this);
        _dragging = true;
        Show(_start, _start);
        e.Handled = true;
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_dragging)
        {
            Show(_start, e.GetPosition(this));
        }
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging || DataContext is not AreaPromptModel model)
        {
            return;
        }

        _dragging = false;
        var end = e.GetPosition(this);
        if (model.PickPoint)
        {
            model.Choose(new Box((int)Math.Floor(end.X), (int)Math.Floor(end.Y), 1, 1));
            e.Handled = true;
            return;
        }

        var box = Normalize(_start, end, model.Width, model.Height);
        Rubber.IsVisible = false;
        if (box.Width >= 2 && box.Height >= 2)
        {
            model.Choose(box);
        }

        e.Handled = true;
    }

    private void Show(AvaloniaPoint a, AvaloniaPoint b)
    {
        var rubber = Rubber;
        var left = Math.Min(a.X, b.X);
        var top = Math.Min(a.Y, b.Y);
        Canvas.SetLeft(rubber, left);
        Canvas.SetTop(rubber, top);
        rubber.Width = Math.Abs(a.X - b.X);
        rubber.Height = Math.Abs(a.Y - b.Y);
        rubber.IsVisible = DataContext is AreaPromptModel { PickPoint: false };
    }

    internal static Box Normalize(AvaloniaPoint a, AvaloniaPoint b, int width, int height)
    {
        var x1 = Math.Max(0, (int)Math.Floor(Math.Min(a.X, b.X)));
        var y1 = Math.Max(0, (int)Math.Floor(Math.Min(a.Y, b.Y)));
        var x2 = Math.Min(width, (int)Math.Ceiling(Math.Max(a.X, b.X)));
        var y2 = Math.Min(height, (int)Math.Ceiling(Math.Max(a.Y, b.Y)));
        return new Box(x1, y1, x2 - x1, y2 - y1);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not AreaPromptModel model)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            model.Cancel.Execute(null);
            e.Handled = true;
        }
        else if (e.Key is Key.Enter or Key.Return && !model.PickPoint)
        {
            model.ChooseEverything();
            e.Handled = true;
        }
    }
}
