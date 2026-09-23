using Avalonia.Controls;
using AvaWin.Controls;

namespace EightWm;

public sealed partial class StartView : UserControl
{
    private readonly VerticalFlingRecognizer _fling = new() { IgnoreItems = true };
    private StartModel? _model;

    public StartView()
    {
        InitializeComponent();
        Tiles.GestureRecognizers.Add(_fling);
        _fling.Flung += speed => _model?.RequestApps(speed < 0);
        Tiles.ItemInvoked += (_, e) =>
        {
            if (e.Item is Tile tile)
            {
                _model?.Invoke(tile);
            }
        };
    }

    public ListView TileList => Tiles;

    public ListView GroupsList => GroupList;

    public SemanticZoom SemanticZoom => Zoom;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _model = DataContext as StartModel;
        if (_model is { } model)
        {
            Tiles.Layout = new CellSpanningLayout
            {
                ItemInfo = model.ItemInfo,
                GroupInfo = _ => new GroupInfo(true, Tile.Cell, Tile.Cell),
                GroupHeaderPosition = GroupHeaderPosition.Top,
            };
        }
    }
}
