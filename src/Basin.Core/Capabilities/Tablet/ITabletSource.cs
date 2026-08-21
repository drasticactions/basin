namespace Basin.Capabilities;

public interface ITabletSource
{
    int EnumerateTablets(Span<TabletInfo> tablets);

    int EnumerateTools(Span<TabletToolInfo> tools);

    int EnumeratePads(Span<TabletPadInfo> pads);

    event Action<TabletInfo>? TabletAdded;

    event Action<ulong>? TabletRemoved;

    event Action<TabletToolInfo>? ToolAdded;

    event Action<ulong>? ToolRemoved;

    event Action<TabletPadInfo>? PadAdded;

    event Action<ulong>? PadRemoved;

    void AddObserver(ITabletObserver observer);

    void RemoveObserver(ITabletObserver observer);
}
