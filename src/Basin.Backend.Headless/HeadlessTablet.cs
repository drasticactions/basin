using Basin.Capabilities;

namespace Basin.Backend.Headless;

/// <summary>
/// A tablet source whose tablets, tools and pads the caller adds and removes by hand.
/// Tablets, tools and pads share one ID counter that starts at 1.
/// </summary>
public sealed class HeadlessTablet : ITabletSource
{
    private readonly List<TabletInfo> _tablets = [];
    private readonly List<TabletToolInfo> _tools = [];
    private readonly List<TabletPadInfo> _pads = [];
    private ulong _nextId = 1;

    internal HeadlessTablet()
    {
    }

    /// <inheritdoc/>
    public event Action<TabletInfo>? TabletAdded;

    /// <inheritdoc/>
    public event Action<ulong>? TabletRemoved;

    /// <inheritdoc/>
    public event Action<TabletToolInfo>? ToolAdded;

    /// <inheritdoc/>
    public event Action<ulong>? ToolRemoved;

    /// <inheritdoc/>
    public event Action<TabletPadInfo>? PadAdded;

    /// <inheritdoc/>
    public event Action<ulong>? PadRemoved;

    private readonly TabletObservers _tabletObservers = new();

    /// <inheritdoc/>
    public void AddObserver(ITabletObserver observer) => _tabletObservers.Add(observer);

    /// <inheritdoc/>
    public void RemoveObserver(ITabletObserver observer) => _tabletObservers.Remove(observer);

    /// <summary>
    /// Adds a tablet and raises <see cref="TabletAdded"/>. Its device path is <c>/dev/input/</c> followed by <paramref name="name"/>.
    /// </summary>
    /// <param name="name">The tablet name.</param>
    /// <param name="vendorId">The USB vendor ID.</param>
    /// <param name="productId">The USB product ID.</param>
    /// <param name="busType">The bus type.</param>
    /// <returns>The tablet description with its new ID.</returns>
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

    /// <summary>
    /// Adds a tool and raises <see cref="ToolAdded"/>.
    /// </summary>
    /// <param name="type">The tool type.</param>
    /// <param name="hardwareSerial">The hardware serial number. Zero means unknown.</param>
    /// <param name="axes">The axes that the tool reports.</param>
    /// <returns>The tool description with its new ID.</returns>
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

    /// <summary>
    /// Adds a pad and raises <see cref="PadAdded"/>.
    /// </summary>
    /// <param name="buttons">The number of pad buttons.</param>
    /// <param name="path">The device path. If <see langword="null"/>, the path is <c>/dev/input/headless-pad</c>.</param>
    /// <param name="dials">The number of dials.</param>
    /// <returns>The pad description with its new ID.</returns>
    public TabletPadInfo AddPad(uint buttons = 4, string? path = null, uint dials = 0)
    {
        var info = new TabletPadInfo(_nextId++, path ?? "/dev/input/headless-pad", buttons, dials);
        _pads.Add(info);
        PadAdded?.Invoke(info);
        return info;
    }

    /// <summary>
    /// Removes the tablet and raises <see cref="TabletRemoved"/>. The event occurs also when no tablet has the ID.
    /// </summary>
    /// <param name="id">The tablet ID.</param>
    public void RemoveTablet(ulong id)
    {
        _tablets.RemoveAll(t => t.Id == id);
        TabletRemoved?.Invoke(id);
    }

    /// <summary>
    /// Removes the tool and raises <see cref="ToolRemoved"/>. The event occurs also when no tool has the ID.
    /// </summary>
    /// <param name="id">The tool ID.</param>
    public void RemoveTool(ulong id)
    {
        _tools.RemoveAll(t => t.Id == id);
        ToolRemoved?.Invoke(id);
    }

    /// <summary>
    /// Removes the pad and raises <see cref="PadRemoved"/>. The event occurs also when no pad has the ID.
    /// </summary>
    /// <param name="id">The pad ID.</param>
    public void RemovePad(ulong id)
    {
        _pads.RemoveAll(p => p.Id == id);
        PadRemoved?.Invoke(id);
    }

    /// <inheritdoc/>
    public int EnumerateTablets(Span<TabletInfo> tablets) => Copy(_tablets, tablets);

    /// <inheritdoc/>
    public int EnumerateTools(Span<TabletToolInfo> tools) => Copy(_tools, tools);

    /// <inheritdoc/>
    public int EnumeratePads(Span<TabletPadInfo> pads) => Copy(_pads, pads);

    /// <summary>
    /// Sends a proximity change of a tool to every observer.
    /// </summary>
    /// <param name="toolId">The tool ID.</param>
    /// <param name="tabletId">The ID of the tablet that the tool is near.</param>
    /// <param name="inProximity"><see langword="true"/> when the tool comes into proximity.</param>
    public void InjectProximity(ulong toolId, ulong tabletId, bool inProximity) =>
        _tabletObservers.ToolProximity(toolId, tabletId, inProximity);

    /// <summary>
    /// Sends the axis values of a tool to every observer.
    /// </summary>
    /// <param name="toolId">The tool ID.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="axes">The axis values.</param>
    public void InjectAxis(ulong toolId, uint timeMs, in TabletToolAxes axes) =>
        _tabletObservers.ToolAxis(toolId, timeMs, axes);

    /// <summary>
    /// Sends a tool button event to every observer.
    /// </summary>
    /// <param name="toolId">The tool ID.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="button">The button code.</param>
    /// <param name="pressed"><see langword="true"/> for a press, <see langword="false"/> for a release.</param>
    public void InjectButton(ulong toolId, uint timeMs, uint button, bool pressed) =>
        _tabletObservers.ToolButton(toolId, timeMs, button, pressed);

    /// <summary>
    /// Sends a pad event to every observer.
    /// </summary>
    /// <param name="padId">The pad ID.</param>
    /// <param name="timeMs">The event time in milliseconds.</param>
    /// <param name="padEvent">The pad event.</param>
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
