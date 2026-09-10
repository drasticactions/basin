using Microsoft.Maui.Controls;
using AC = Avalonia.Controls;

namespace MauiComp.Shell;

public partial class StartMenuPage
{
    public StartMenuPage()
    {
        Resources.Add("Banner", new BannerDrawable());
        InitializeComponent();
    }

    public MenuFlyout? Programs { get; set; }

    public bool IsProgramsOpen =>
        Programs?.Handler?.PlatformView is AC.ContextMenu { IsOpen: true };

    public void ShowPrograms(double availableHeight)
    {
        if (Programs is not { } flyout)
        {
            return;
        }

        if (!ReferenceEquals(FlyoutBase.GetContextFlyout(ProgramsRow), flyout))
        {
            FlyoutBase.SetContextFlyout(ProgramsRow, flyout);
        }

        if (flyout.Handler?.PlatformView is not AC.ContextMenu menu ||
            ProgramsRow.Handler?.PlatformView is not AC.Control row ||
            MenuFrame.Handler?.PlatformView is not AC.Control target)
        {
            return;
        }

        if (!target.IsLoaded)
        {
            void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
            {
                target.Loaded -= OnLoaded;
                ShowPrograms(availableHeight);
            }

            target.Loaded += OnLoaded;
            return;
        }

        menu.Placement = AC.PlacementMode.AnchorAndGravity;
        menu.PlacementAnchor = Avalonia.Controls.Primitives.PopupPositioning.PopupAnchor.BottomRight;
        menu.PlacementGravity = Avalonia.Controls.Primitives.PopupPositioning.PopupGravity.TopRight;
        menu.PlacementTarget = target;
        menu.MaxHeight = availableHeight;
        menu.Open(row);
    }

    public void HidePrograms()
    {
        if (Programs?.Handler?.PlatformView is AC.ContextMenu menu)
        {
            menu.Close();
        }
    }

    private void OnRowEntered(object? sender, PointerEventArgs e)
    {
        if (sender is Border row)
        {
            row.Background = RoyaleTheme.Brush("HighlightBrush");
            foreach (var label in Descendants<Label>(row))
            {
                label.TextColor = Microsoft.Maui.Graphics.Colors.White;
            }
        }
    }

    private void OnRowExited(object? sender, PointerEventArgs e)
    {
        if (sender is Border row)
        {
            row.Background = RoyaleTheme.Brush("TransparentBrush");
            foreach (var label in Descendants<Label>(row))
            {
                label.TextColor = Microsoft.Maui.Graphics.Color.FromRgb(0, 0, 0);
            }
        }
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
}
