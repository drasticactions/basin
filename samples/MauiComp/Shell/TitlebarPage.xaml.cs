namespace MauiComp.Shell;

public partial class TitlebarPage
{
    public TitlebarPage()
    {
        InitializeComponent();
    }

    public bool Frosted
    {
        get => ((BrushSwitchConverter)Resources["CaptionBrush"]).Frosted;
        set
        {
            var converter = (BrushSwitchConverter)Resources["CaptionBrush"];
            if (converter.Frosted == value)
            {
                return;
            }

            converter.Frosted = value;
            var context = BindingContext;
            BindingContext = null;
            BindingContext = context;
        }
    }
}
