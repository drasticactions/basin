namespace MauiComp.Shell;

public partial class SwitcherPage
{
    public SwitcherPage()
    {
        Resources.Add("SelectedText", new SelectedTextConverter());
        InitializeComponent();
    }

    private bool _frosted;

    public bool Frosted
    {
        get => _frosted;
        set
        {
            _frosted = value;
            SwitcherFrame.Background = RoyaleTheme.Brush(value ? "SwitcherBrushFrost" : "SwitcherBrush");
        }
    }
}
