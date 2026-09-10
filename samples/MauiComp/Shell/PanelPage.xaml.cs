using Microsoft.Maui.Controls;
using AC = Avalonia.Controls;

namespace MauiComp.Shell;

public partial class PanelPage
{
    public PanelPage()
    {
        InitializeComponent();
    }

    public double StartButtonWidth => LauncherButton.Width;

    public Microsoft.Maui.Graphics.Rect? TaskBox(TaskEntry entry)
    {
        foreach (var border in Descendants<Border>(this))
        {
            if (!ReferenceEquals(border.BindingContext, entry) ||
                border.Handler?.PlatformView is not AC.Control control ||
                Handler?.PlatformView is not AC.Control root ||
                Avalonia.VisualExtensions.TranslatePoint(control, new Avalonia.Point(0, 0), root) is not { } origin)
            {
                continue;
            }

            return new Microsoft.Maui.Graphics.Rect(origin.X, origin.Y, control.Bounds.Width, control.Bounds.Height);
        }

        return null;
    }

    private static IEnumerable<T> Descendants<T>(Element root)
        where T : Element
    {
        foreach (var child in ((Microsoft.Maui.IVisualTreeElement)root).GetVisualChildren())
        {
            if (child is T match)
            {
                yield return match;
            }

            if (child is Element element)
            {
                foreach (var nested in Descendants<T>(element))
                {
                    yield return nested;
                }
            }
        }
    }

    private void OnLauncherTapped(object? sender, TappedEventArgs e) =>
        (BindingContext as PanelModel)?.StartRequested?.Invoke();
}
