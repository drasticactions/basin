using Basin.Capabilities;

namespace Basin.Backend.Headless;

public sealed class HeadlessTablet : ITabletSource
{
    private readonly List<TabletInfo> _tablets = [];
    private readonly List<TabletToolInfo> _tools = [];
    private readonly List<TabletPadInfo> _pads = [];
    private ulong _nextId = 1;

    internal HeadlessTablet()
    {
    }

    public event Action<TabletInfo>? TabletAdded;

    public event Action<ulong>? TabletRemoved;

    public event Action<TabletToolInfo>? ToolAdded;

    public event Action<ulong>? ToolRemoved;

    public event Action<TabletPadInfo>? PadAdded;

    public event Action<ulong>? PadRemoved;

    private readonly TabletObservers _tabletObservers = new();

    public void AddObserver(ITabletObserver observer) => _tabletObservers.Add(observer);

    public void RemoveObserver(ITabletObserver observer) => _tabletObservers.Remove(observer);

    public TabletInfo AddTablet(
        string name = "headless-tablet",
        uint vendorId = 0,
        uint productId = 0,
        uint busType = 0)
    {
        var info = new TabletInfo(_nextId++, name, vendorId, productId, $"/dev/input/{name}", busType);
        _tablets.Add(info);
        TabletAdded?.Invoke(info);
        return info;
    }

    public TabletToolInfo AddTool(
        TabletToolType type = TabletToolType.Pen,
        ulong hardwareSerial = 0,
        TabletToolAxis axes = TabletToolAxis.Pressure | TabletToolAxis.Tilt | TabletToolAxis.Distance)
    {
        var info = new TabletToolInfo(_nextId++, type, hardwareSerial, axes);
        _tools.Add(info);
        ToolAdded?.Invoke(info);
        return info;
    }

    public TabletPadInfo AddPad(uint buttons = 4, string? path = null, uint dials = 0)
    {
        var info = new TabletPadInfo(_nextId++, path ?? "/dev/input/headless-pad", buttons, dials);
        _pads.Add(info);
        PadAdded?.Invoke(info);
        return info;
    }

    public void RemoveTablet(ulong id)
    {
        _tablets.RemoveAll(t => t.Id == id);
        TabletRemoved?.Invoke(id);
    }

    public void RemoveTool(ulong id)
    {
        _tools.RemoveAll(t => t.Id == id);
        ToolRemoved?.Invoke(id);
    }

    public void RemovePad(ulong id)
    {
        _pads.RemoveAll(p => p.Id == id);
        PadRemoved?.Invoke(id);
    }

    public int EnumerateTablets(Span<TabletInfo> tablets) => Copy(_tablets, tablets);

    public int EnumerateTools(Span<TabletToolInfo> tools) => Copy(_tools, tools);

    public int EnumeratePads(Span<TabletPadInfo> pads) => Copy(_pads, pads);

    public void InjectProximity(ulong toolId, ulong tabletId, bool inProximity) =>
        _tabletObservers.ToolProximity(toolId, tabletId, inProximity);

    public void InjectAxis(ulong toolId, uint timeMs, in TabletToolAxes axes) =>
        _tabletObservers.ToolAxis(toolId, timeMs, axes);

    public void InjectButton(ulong toolId, uint timeMs, uint button, bool pressed) =>
        _tabletObservers.ToolButton(toolId, timeMs, button, pressed);

    public void InjectPad(ulong padId, uint timeMs, in TabletPadEvent padEvent) =>
        _tabletObservers.PadEvent(padId, timeMs, padEvent);

    private static int Copy<T>(List<T> source, Span<T> target)
    {
        if (target.Length < source.Count)
        {
            return -1;
        }

        for (var i = 0; i < source.Count; i++)
        {
            target[i] = source[i];
        }

        return source.Count;
    }
}
