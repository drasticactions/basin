namespace MauiComp.Shell;

public partial class SwitcherPage
{
    public SwitcherPage()
    {
        Resources.Add("SelectedText", new SelectedTextConverter());
        InitializeComponent();
    }
}
