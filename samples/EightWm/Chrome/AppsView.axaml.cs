using Avalonia.Controls;
using AvaWin.Controls;

namespace EightWm;

public sealed partial class AppsView : UserControl
{
    private readonly VerticalFlingRecognizer _fling = new();
    private StartModel? _model;

    public AppsView()
    {
        InitializeComponent();
        Root.GestureRecognizers.Add(_fling);
        _fling.Flung += speed => _model?.RequestApps(speed < 0);
        AppList.ItemInvoked += (_, e) =>
        {
            if (e.Item is Tile tile)
            {
                _model?.Invoke(tile);
            }
        };
    }

    public ListView List => AppList;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _model = DataContext as StartModel;
    }
}
