using AC = Avalonia.Controls;

namespace MauiComp.Shell;

public partial class RunPage
{
    public RunPage()
    {
        InitializeComponent();
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        if (BindingContext is RunModel model)
        {
            model.TextSource = () => (CommandEntry.Handler?.PlatformView as AC.TextBox)?.Text ?? CommandEntry.Text;
        }
    }

    public void FocusEntry()
    {
        if (CommandEntry.Handler?.PlatformView is not AC.Control control)
        {
            return;
        }

        if (control.IsLoaded)
        {
            control.Focus();
            return;
        }

        control.Loaded += OnEntryLoaded;
    }

    private void OnEntryLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is AC.Control control)
        {
            control.Loaded -= OnEntryLoaded;
            control.Focus();
        }
    }
}
