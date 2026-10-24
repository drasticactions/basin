using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

/// <summary>
/// Serves the <c>zwp_tablet_manager_v2</c> global. It announces every tablet, tool and pad to every tablet seat, and sends tool and pad events to the client of the focused surface.
/// </summary>
public sealed class TabletManager : ITabletObserver, IDisposable
{
    /// <summary>
    /// The highest global version that the manager advertises.
    /// </summary>
    public const int Version = 2;

    private readonly WlServerDisplay _display;
    private readonly WlGlobal _global;
    private readonly List<SeatBinding> _bindings = [];
    private readonly List<TabletDevice> _tablets = [];
    private readonly List<TabletTool> _tools = [];
    private readonly List<TabletPad> _pads = [];

    private readonly ITabletSource? _source;
    private readonly Dictionary<ulong, TabletDevice> _sourceTablets = [];
    private readonly Dictionary<ulong, TabletTool> _sourceTools = [];
    private readonly Dictionary<ulong, TabletPad> _sourcePads = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="TabletManager"/> class and creates the global. With a source, it adopts the devices that exist and follows the source for later changes.
    /// </summary>
    /// <param name="display">The display to create the global on.</param>
    /// <param name="source">The device source to follow. When it is <see langword="null"/>, the consumer adds devices with <see cref="AddTablet"/>, <see cref="AddTool"/> and <see cref="AddPad"/>.</param>
    public TabletManager(WlServerDisplay display, ITabletSource? source = null)
    {
        ArgumentNullException.ThrowIfNull(display);
        _display = display;
        _source = source;
        _global = display.CreateGlobal(ZwpTabletManagerV2.Interface, Version, OnBind);
        if (_source is { } live)
        {
            live.TabletAdded += OnTabletAdded;
            live.TabletRemoved += OnTabletRemoved;
            live.ToolAdded += OnToolAdded;
            live.ToolRemoved += OnToolRemoved;
            live.PadAdded += OnPadAdded;
            live.PadRemoved += OnPadRemoved;
            live.AddObserver(this);
            AdoptExisting(live);
        }
    }

    /// <summary>
    /// Stops following the source and removes the global.
    /// </summary>
    public void Dispose()
    {
        if (_source is { } live)
        {
            live.TabletAdded -= OnTabletAdded;
            live.TabletRemoved -= OnTabletRemoved;
            live.ToolAdded -= OnToolAdded;
            live.ToolRemoved -= OnToolRemoved;
            live.PadAdded -= OnPadAdded;
            live.PadRemoved -= OnPadRemoved;
            live.RemoveObserver(this);
        }

        _global.Dispose();
    }

    /// <summary>
    /// Finds a tool that the source added.
    /// </summary>
    /// <param name="id">The tool id from the source.</param>
    /// <returns>The tool, or <see langword="null"/> when the source has no tool with that id.</returns>
    public TabletTool? ToolFor(ulong id) => _sourceTools.GetValueOrDefault(id);

    /// <summary>
    /// Finds a tablet that the source added.
    /// </summary>
    /// <param name="id">The tablet id from the source.</param>
    /// <returns>The tablet, or <see langword="null"/> when the source has no tablet with that id.</returns>
    public TabletDevice? TabletFor(ulong id) => _sourceTablets.GetValueOrDefault(id);

    /// <summary>
    /// Finds a pad that the source added.
    /// </summary>
    /// <param name="id">The pad id from the source.</param>
    /// <returns>The pad, or <see langword="null"/> when the source has no pad with that id.</returns>
    public TabletPad? PadFor(ulong id) => _sourcePads.GetValueOrDefault(id);

    private void AdoptExisting(ITabletSource source)
    {
        var tablets = new TabletInfo[8];
        var count = source.EnumerateTablets(tablets);
        while (count < 0)
        {
            tablets = new TabletInfo[tablets.Length * 2];
            count = source.EnumerateTablets(tablets);
        }

        for (var i = 0; i < count; i++)
        {
            OnTabletAdded(tablets[i]);
        }

        var tools = new TabletToolInfo[8];
        count = source.EnumerateTools(tools);
        while (count < 0)
        {
            tools = new TabletToolInfo[tools.Length * 2];
            count = source.EnumerateTools(tools);
        }

        for (var i = 0; i < count; i++)
        {
            OnToolAdded(tools[i]);
        }

        var pads = new TabletPadInfo[8];
        count = source.EnumeratePads(pads);
        while (count < 0)
        {
            pads = new TabletPadInfo[pads.Length * 2];
            count = source.EnumeratePads(pads);
        }

        for (var i = 0; i < count; i++)
        {
            OnPadAdded(pads[i]);
        }
    }

    private void OnTabletAdded(TabletInfo info) =>
        _sourceTablets[info.Id] = AddTablet(info.Name, info.VendorId, info.ProductId, info.Path, info.BusType);

    private void OnTabletRemoved(ulong id)
    {
        if (_sourceTablets.Remove(id, out var tablet))
        {
            tablet.Remove();
        }
    }

    private void OnToolAdded(TabletToolInfo info) =>
        _sourceTools[info.Id] = AddTool((ZwpTabletToolV2.Type)info.Type, info.HardwareSerial, (TabletToolCapabilities)info.Axes);

    private void OnToolRemoved(ulong id)
    {
        if (_sourceTools.Remove(id, out var tool))
        {
            tool.Remove();
        }
    }

    private void OnPadAdded(TabletPadInfo info) => _sourcePads[info.Id] = AddPad(info.Path, info.Buttons, info.Dials);

    private void OnPadRemoved(ulong id)
    {
        if (_sourcePads.Remove(id, out var pad))
        {
            pad.Remove();
        }
    }

    /// <summary>
    /// Occurs when a source tool comes into proximity of a tablet. The axes are the last ones that the tool reported, or zero. The handler gives the tool focus, for example with <see cref="TabletAiming.AimAt"/>.
    /// </summary>
    public event Action<TabletTool, TabletDevice, TabletToolAxes>? ToolProximityIn;

    /// <summary>
    /// Occurs when a source tool in proximity reports new axes, before the axes go to the client. The manager sends no motion itself, so the handler moves the focus.
    /// </summary>
    public event Action<TabletTool, TabletToolAxes>? ToolMoved;

    /// <summary>
    /// Occurs for each button or dial event of a source pad, before the event goes to the client.
    /// </summary>
    public event Action<TabletPad, TabletPadEvent>? PadActivity;

    /// <inheritdoc/>
    public void OnToolProximity(ulong toolId, ulong tabletId, bool inProximity)
    {
        if (_sourceTools.GetValueOrDefault(toolId) is not { } tool)
        {
            return;
        }

        if (!inProximity)
        {
            tool.NotifyProximityOut();
            tool.NotifyFrame(tool.LastTimeMs);
            _toolAxes.Remove(toolId);
            return;
        }

        if (_sourceTablets.GetValueOrDefault(tabletId) is { } tablet)
        {
            tool.EnterProximity(tablet);
            ToolProximityIn?.Invoke(tool, tablet, _toolAxes.GetValueOrDefault(toolId));
        }
    }

    /// <inheritdoc/>
    public void OnToolAxis(ulong toolId, uint timeMs, TabletToolAxes axes)
    {
        if (_sourceTools.GetValueOrDefault(toolId) is not { } tool)
        {
            return;
        }

        _toolAxes[toolId] = axes;
        tool.LastTimeMs = timeMs;
        if (tool.Tablet is null)
        {
            return;
        }

        ToolMoved?.Invoke(tool, axes);
        tool.NotifyAxes(axes);
        tool.NotifyFrame(timeMs);
    }

    /// <inheritdoc/>
    public void OnToolButton(ulong toolId, uint timeMs, uint button, bool pressed)
    {
        if (_sourceTools.GetValueOrDefault(toolId) is not { } tool)
        {
            return;
        }

        tool.LastTimeMs = timeMs;
        if (button == BtnTouch)
        {
            if (pressed)
            {
                tool.NotifyDown();
            }
            else
            {
                tool.NotifyUp();
            }
        }
        else
        {
            tool.NotifyButton(button, pressed);
        }

        tool.NotifyFrame(timeMs);
    }

    /// <inheritdoc/>
    public void OnPadEvent(ulong padId, uint timeMs, TabletPadEvent padEvent)
    {
        if (_sourcePads.GetValueOrDefault(padId) is not { } pad)
        {
            return;
        }

        PadActivity?.Invoke(pad, padEvent);
        switch (padEvent.Kind)
        {
            case TabletPadEventKind.Button:
                pad.NotifyButton(timeMs, padEvent.Index, padEvent.Pressed);
                break;
            case TabletPadEventKind.Dial:
                pad.NotifyDial(timeMs, padEvent.Index, (int)padEvent.Value);
                break;
        }
    }

    private const uint BtnTouch = 0x14a;

    private readonly Dictionary<ulong, TabletToolAxes> _toolAxes = [];

    /// <summary>
    /// Adds a tablet and announces it to every bound tablet seat.
    /// </summary>
    /// <param name="name">The device name.</param>
    /// <param name="vendorId">The USB vendor id.</param>
    /// <param name="productId">The USB product id.</param>
    /// <param name="path">The device path, for example <c>/dev/input/event5</c>.</param>
    /// <param name="busType">The bus type from <c>linux/input.h</c>, or zero to send none.</param>
    /// <returns>The new tablet. Call <see cref="TabletDevice.Remove"/> to remove it.</returns>
    public TabletDevice AddTablet(string name, uint vendorId, uint productId, string path, uint busType = 0)
    {
        var tablet = new TabletDevice(this, name, vendorId, productId, path, busType);
        _tablets.Add(tablet);
        foreach (var binding in Live())
        {
            binding.Announce(tablet);
        }

        return tablet;
    }

    /// <summary>
    /// Adds a tool and announces it to every bound tablet seat.
    /// </summary>
    /// <param name="type">The tool type, for example a pen or an eraser.</param>
    /// <param name="hardwareSerial">The serial number of the tool, or zero when the hardware has none.</param>
    /// <param name="capabilities">The axes that the tool reports.</param>
    /// <returns>The new tool. Call <see cref="TabletTool.Remove"/> to remove it.</returns>
    public TabletTool AddTool(ZwpTabletToolV2.Type type, ulong hardwareSerial, TabletToolCapabilities capabilities)
    {
        var tool = new TabletTool(this, type, hardwareSerial, capabilities);
        _tools.Add(tool);
        foreach (var binding in Live())
        {
            binding.Announce(tool);
        }

        return tool;
    }

    /// <summary>
    /// Adds a pad with one group and one mode, and announces it to every bound tablet seat.
    /// </summary>
    /// <param name="path">The device path.</param>
    /// <param name="buttons">The number of pad buttons.</param>
    /// <param name="dials">The number of dials. Clients see dials only from version 2 of the global.</param>
    /// <returns>The new pad. Call <see cref="TabletPad.Remove"/> to remove it.</returns>
    public TabletPad AddPad(string path, uint buttons, uint dials = 0)
    {
        var pad = new TabletPad(this, path, buttons, dials);
        _pads.Add(pad);
        foreach (var binding in Live())
        {
            binding.Announce(pad);
        }

        return pad;
    }

    private IEnumerable<SeatBinding> Live()
    {
        for (var i = _bindings.Count - 1; i >= 0; i--)
        {
            if (_bindings[i].Resource.IsDestroyed)
            {
                _bindings.RemoveAt(i);
            }
            else
            {
                yield return _bindings[i];
            }
        }
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new ZwpTabletManagerV2Resource(client, version, id);
        manager.GetTabletSeat += (_, e) =>
        {
            var seatResource = new ZwpTabletSeatV2Resource(client, manager.Version, e.TabletSeat);
            var binding = new SeatBinding(this, seatResource);
            _bindings.Add(binding);

            foreach (var tablet in _tablets)
            {
                binding.Announce(tablet);
            }

            foreach (var tool in _tools)
            {
                binding.Announce(tool);
            }

            foreach (var pad in _pads)
            {
                binding.Announce(pad);
            }
        };
    }

    private sealed class SeatBinding
    {
        private readonly TabletManager _owner;

        public SeatBinding(TabletManager owner, ZwpTabletSeatV2Resource resource)
        {
            _owner = owner;
            Resource = resource;
        }

        public ZwpTabletSeatV2Resource Resource { get; }

        public Dictionary<TabletDevice, ZwpTabletV2Resource> Tablets { get; } = [];

        public Dictionary<TabletTool, ZwpTabletToolV2Resource> Tools { get; } = [];

        public Dictionary<TabletPad, ZwpTabletPadV2Resource> Pads { get; } = [];

        public Dictionary<TabletPad, ZwpTabletPadDialV2Resource[]> PadDials { get; } = [];

        public void Announce(TabletDevice tablet)
        {
            var resource = new ZwpTabletV2Resource(Resource.Client, Resource.Version, 0);
            Resource.SendTabletAdded(resource);
            resource.SendName(tablet.Name);
            resource.SendId(tablet.VendorId, tablet.ProductId);
            resource.SendPath(tablet.Path);
            if (resource.SupportsSendBustype && tablet.BusType != 0)
            {
                resource.SendBustype((ZwpTabletV2.Bustype)tablet.BusType);
            }

            resource.SendDone();
            Tablets[tablet] = resource;
            resource.Destroyed += (_, _) => Tablets.Remove(tablet);
        }

        public void Announce(TabletTool tool)
        {
            var resource = new ZwpTabletToolV2Resource(Resource.Client, Resource.Version, 0);
            Resource.SendToolAdded(resource);
            resource.SendType(tool.Type);
            resource.SendHardwareSerial((uint)(tool.HardwareSerial >> 32), (uint)tool.HardwareSerial);
            foreach (var capability in tool.CapabilityList())
            {
                resource.SendCapability(capability);
            }

            resource.SendDone();
            Tools[tool] = resource;
            resource.Destroyed += (_, _) => Tools.Remove(tool);
        }

        public void Announce(TabletPad pad)
        {
            var resource = new ZwpTabletPadV2Resource(Resource.Client, Resource.Version, 0);
            Resource.SendPadAdded(resource);
            var group = new ZwpTabletPadGroupV2Resource(Resource.Client, Resource.Version, 0);
            resource.SendGroup(group);
            group.SendModes(1);
            if (group.SupportsSendDial && pad.Dials > 0)
            {
                var dials = new ZwpTabletPadDialV2Resource[pad.Dials];
                for (var i = 0; i < dials.Length; i++)
                {
                    dials[i] = new ZwpTabletPadDialV2Resource(Resource.Client, Resource.Version, 0);
                    group.SendDial(dials[i]);
                }

                PadDials[pad] = dials;
            }

            group.SendDone();
            resource.SendPath(pad.Path);
            resource.SendButtons(pad.Buttons);
            resource.SendDone();
            Pads[pad] = resource;
            resource.Destroyed += (_, _) =>
            {
                Pads.Remove(pad);
                PadDials.Remove(pad);
            };
        }
    }

    /// <summary>
    /// Identifies the axes that a tablet tool reports. Combined flags mean that the tool reports each of the axes.
    /// </summary>
    [Flags]
    public enum TabletToolCapabilities : uint
    {
        /// <summary>
        /// The tool reports tilt.
        /// </summary>
        Tilt = 1 << 0,
        /// <summary>
        /// The tool reports tip pressure.
        /// </summary>
        Pressure = 1 << 1,
        /// <summary>
        /// The tool reports hover distance.
        /// </summary>
        Distance = 1 << 2,
        /// <summary>
        /// The tool reports rotation.
        /// </summary>
        Rotation = 1 << 3,
        /// <summary>
        /// The tool reports a slider position.
        /// </summary>
        Slider = 1 << 4,
        /// <summary>
        /// The tool reports wheel movement.
        /// </summary>
        Wheel = 1 << 5,
    }

    /// <summary>
    /// A tablet that the manager announces to clients.
    /// </summary>
    public sealed class TabletDevice
    {
        private readonly TabletManager _owner;

        internal TabletDevice(
            TabletManager owner,
            string name,
            uint vendorId,
            uint productId,
            string path,
            uint busType)
        {
            _owner = owner;
            Name = name;
            VendorId = vendorId;
            ProductId = productId;
            Path = path;
            BusType = busType;
        }

        /// <summary>
        /// Gets the device name.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the USB vendor id.
        /// </summary>
        public uint VendorId { get; }

        /// <summary>
        /// Gets the USB product id.
        /// </summary>
        public uint ProductId { get; }

        /// <summary>
        /// Gets the device path.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Gets the bus type from <c>linux/input.h</c>. Zero means that no bus type is sent.
        /// </summary>
        public uint BusType { get; }

        /// <summary>
        /// Removes the tablet and sends <c>removed</c> to every client.
        /// </summary>
        public void Remove()
        {
            _owner._tablets.Remove(this);
            foreach (var binding in _owner.Live())
            {
                if (binding.Tablets.TryGetValue(this, out var resource) && !resource.IsDestroyed)
                {
                    resource.SendRemoved();
                }
            }
        }
    }

    /// <summary>
    /// A tablet tool that the manager announces to clients. Its events go only to the client of the surface that has the tool focus.
    /// </summary>
    public sealed class TabletTool
    {
        private readonly TabletManager _owner;
        private Surface? _surface;
        private bool _leaving;

        internal TabletTool(TabletManager owner, ZwpTabletToolV2.Type type, ulong hardwareSerial, TabletToolCapabilities capabilities)
        {
            _owner = owner;
            Type = type;
            HardwareSerial = hardwareSerial;
            Capabilities = capabilities;
        }

        /// <summary>
        /// Gets the tool type.
        /// </summary>
        public ZwpTabletToolV2.Type Type { get; }

        /// <summary>
        /// Gets the serial number of the tool, or zero when the hardware has none.
        /// </summary>
        public ulong HardwareSerial { get; }

        /// <summary>
        /// Gets the axes that the tool reports. Axis values for other axes are not sent.
        /// </summary>
        public TabletToolCapabilities Capabilities { get; }

        /// <summary>
        /// Removes the tool and sends <c>removed</c> to every client.
        /// </summary>
        public void Remove()
        {
            _owner._tools.Remove(this);
            foreach (var binding in _owner.Live())
            {
                if (binding.Tools.TryGetValue(this, out var resource) && !resource.IsDestroyed)
                {
                    resource.SendRemoved();
                }
            }
        }

        internal IEnumerable<ZwpTabletToolV2.Capability> CapabilityList()
        {
            if (Capabilities.HasFlag(TabletToolCapabilities.Tilt))
            {
                yield return ZwpTabletToolV2.Capability.Tilt;
            }

            if (Capabilities.HasFlag(TabletToolCapabilities.Pressure))
            {
                yield return ZwpTabletToolV2.Capability.Pressure;
            }

            if (Capabilities.HasFlag(TabletToolCapabilities.Distance))
            {
                yield return ZwpTabletToolV2.Capability.Distance;
            }

            if (Capabilities.HasFlag(TabletToolCapabilities.Rotation))
            {
                yield return ZwpTabletToolV2.Capability.Rotation;
            }

            if (Capabilities.HasFlag(TabletToolCapabilities.Slider))
            {
                yield return ZwpTabletToolV2.Capability.Slider;
            }

            if (Capabilities.HasFlag(TabletToolCapabilities.Wheel))
            {
                yield return ZwpTabletToolV2.Capability.Wheel;
            }
        }

        /// <summary>
        /// Gets the tablet that the tool is in proximity of. It is <see langword="null"/> when the tool is out of proximity.
        /// </summary>
        public TabletDevice? Tablet { get; private set; }

        internal void EnterProximity(TabletDevice tablet) => Tablet = tablet;

        internal void NotifyAxes(in TabletToolAxes axes)
        {
            if (Capabilities.HasFlag(TabletToolCapabilities.Pressure))
            {
                NotifyPressure(axes.Pressure);
            }

            if (Capabilities.HasFlag(TabletToolCapabilities.Distance))
            {
                NotifyDistance(axes.Distance);
            }

            if (Capabilities.HasFlag(TabletToolCapabilities.Tilt))
            {
                NotifyTilt(axes.TiltX, axes.TiltY);
            }

            if (Capabilities.HasFlag(TabletToolCapabilities.Rotation))
            {
                NotifyRotation(axes.Rotation);
            }

            if (Capabilities.HasFlag(TabletToolCapabilities.Slider))
            {
                var slider = (int)Math.Clamp(axes.Slider * 65535, -65535, 65535);
                ForCurrent((resource, _) => resource.SendSlider(slider));
            }

            if (Capabilities.HasFlag(TabletToolCapabilities.Wheel))
            {
                var wheel = axes.Wheel;
                ForCurrent((resource, _) => resource.SendWheel(WlFixed.FromDouble(wheel), (int)wheel));
            }
        }

        /// <summary>
        /// Gets the surface that has the tool focus, or <see langword="null"/>.
        /// </summary>
        public Surface? Focus => _surface;

        /// <summary>
        /// Brings the tool into proximity of a tablet and gives it focus on a surface.
        /// </summary>
        /// <param name="tablet">The tablet that the tool is over.</param>
        /// <param name="surface">The surface that gets the tool focus.</param>
        /// <param name="x">The X position in surface-local logical coordinates.</param>
        /// <param name="y">The Y position in surface-local logical coordinates.</param>
        public void NotifyProximityIn(TabletDevice tablet, Surface surface, double x, double y)
        {
            Tablet = tablet;
            SetFocus(surface, x, y);
        }

        /// <summary>
        /// Moves the tool focus. On the same surface, it sends motion only. On a new surface, it sends <c>proximity_out</c> to the old one and <c>proximity_in</c> and motion to the new one. A tool with no tablet enters no surface.
        /// </summary>
        /// <param name="surface">The surface under the tool, or <see langword="null"/> to clear the focus.</param>
        /// <param name="x">The X position in surface-local logical coordinates.</param>
        /// <param name="y">The Y position in surface-local logical coordinates.</param>
        public void SetFocus(Surface? surface, double x, double y)
        {
            if (surface is not null)
            {
                x *= surface.ServerScale;
                y *= surface.ServerScale;
            }

            if (ReferenceEquals(surface, _surface))
            {
                if (surface is not null)
                {
                    ForCurrent((resource, _) => resource.SendMotion(WlFixed.FromDouble(x), WlFixed.FromDouble(y)));
                }

                return;
            }

            if (_surface is not null)
            {
                ForCurrent((resource, _) =>
                {
                    resource.SendProximityOut();
                    resource.SendFrame(LastTimeMs);
                });

                _leaving = false;
            }

            _surface = surface;
            if (surface is null || Tablet is not { } tablet)
            {
                return;
            }

            ForCurrent((resource, binding) =>
            {
                if (binding.Tablets.TryGetValue(tablet, out var tabletResource))
                {
                    resource.SendProximityIn(_owner._display.NextSerial(), tabletResource, surface.Resource);
                    resource.SendMotion(WlFixed.FromDouble(x), WlFixed.FromDouble(y));
                }
            });
        }

        /// <summary>
        /// Gets the time of the last source event for the tool, in milliseconds.
        /// </summary>
        public uint LastTimeMs { get; internal set; }

        /// <summary>
        /// Sends <c>proximity_out</c> and clears <see cref="Tablet"/>. The focus clears at the next <see cref="NotifyFrame"/>.
        /// </summary>
        public void NotifyProximityOut()
        {
            ForCurrent((resource, _) => resource.SendProximityOut());
            _leaving = true;
            Tablet = null;
        }

        /// <summary>
        /// Sends <c>down</c> when the tip touches the tablet.
        /// </summary>
        public void NotifyDown() =>
            ForCurrent((resource, _) => resource.SendDown(_owner._display.NextSerial()));

        /// <summary>
        /// Sends <c>up</c> when the tip leaves the tablet.
        /// </summary>
        public void NotifyUp() => ForCurrent((resource, _) => resource.SendUp());

        /// <summary>
        /// Sends motion on the focused surface.
        /// </summary>
        /// <param name="x">The X position in surface-local logical coordinates.</param>
        /// <param name="y">The Y position in surface-local logical coordinates.</param>
        public void NotifyMotion(double x, double y)
        {
            var scale = _surface?.ServerScale ?? 1.0;
            x *= scale;
            y *= scale;
            ForCurrent((resource, _) => resource.SendMotion(WlFixed.FromDouble(x), WlFixed.FromDouble(y)));
        }

        /// <summary>
        /// Sends the tip pressure.
        /// </summary>
        /// <param name="pressure">The pressure from 0 to 1. Values outside the range are clamped.</param>
        public void NotifyPressure(double pressure) =>
            ForCurrent((resource, _) => resource.SendPressure((uint)Math.Clamp(pressure * 65535, 0, 65535)));

        /// <summary>
        /// Sends the tool tilt.
        /// </summary>
        /// <param name="tiltX">The tilt along the X axis in degrees.</param>
        /// <param name="tiltY">The tilt along the Y axis in degrees.</param>
        public void NotifyTilt(double tiltX, double tiltY) =>
            ForCurrent((resource, _) => resource.SendTilt(WlFixed.FromDouble(tiltX), WlFixed.FromDouble(tiltY)));

        /// <summary>
        /// Sends the hover distance.
        /// </summary>
        /// <param name="distance">The distance from 0 to 1. Values outside the range are clamped.</param>
        public void NotifyDistance(double distance) =>
            ForCurrent((resource, _) => resource.SendDistance((uint)Math.Clamp(distance * 65535, 0, 65535)));

        /// <summary>
        /// Sends the tool rotation.
        /// </summary>
        /// <param name="degrees">The rotation in degrees, clockwise from the rest position.</param>
        public void NotifyRotation(double degrees) =>
            ForCurrent((resource, _) => resource.SendRotation(WlFixed.FromDouble(degrees)));

        /// <summary>
        /// Sends a tool button event.
        /// </summary>
        /// <param name="button">The evdev button code.</param>
        /// <param name="pressed"><see langword="true"/> for a press, <see langword="false"/> for a release.</param>
        public void NotifyButton(uint button, bool pressed) =>
            ForCurrent((resource, _) => resource.SendButton(
                _owner._display.NextSerial(),
                button,
                pressed ? ZwpTabletToolV2.ButtonState.Pressed : ZwpTabletToolV2.ButtonState.Released));

        /// <summary>
        /// Sends <c>frame</c> to end a group of tool events. After <see cref="NotifyProximityOut"/>, it also clears the focus.
        /// </summary>
        /// <param name="timeMs">The event time in milliseconds.</param>
        public void NotifyFrame(uint timeMs)
        {
            ForCurrent((resource, _) => resource.SendFrame(timeMs));
            if (_leaving)
            {
                _leaving = false;
                _surface = null;
            }
        }

        private void ForCurrent(Action<ZwpTabletToolV2Resource, SeatBinding> send)
        {
            if (_surface is null or { IsDestroyed: true })
            {
                return;
            }

            var client = _surface.Resource.Client;
            foreach (var binding in _owner.Live())
            {
                if (binding.Resource.Client == client &&
                    binding.Tools.TryGetValue(this, out var resource) &&
                    !resource.IsDestroyed)
                {
                    send(resource, binding);
                }
            }
        }
    }

    /// <summary>
    /// A tablet pad that the manager announces to clients. Its events go only to the client of the surface that the pad entered.
    /// </summary>
    public sealed class TabletPad
    {
        private readonly TabletManager _owner;
        private Surface? _surface;

        internal TabletPad(TabletManager owner, string path, uint buttons, uint dials)
        {
            _owner = owner;
            Path = path;
            Buttons = buttons;
            Dials = dials;
        }

        /// <summary>
        /// Gets the device path.
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// Gets the number of pad buttons.
        /// </summary>
        public uint Buttons { get; }

        /// <summary>
        /// Gets the number of dials.
        /// </summary>
        public uint Dials { get; }

        /// <summary>
        /// Removes the pad and sends <c>removed</c> to every client.
        /// </summary>
        public void Remove()
        {
            _owner._pads.Remove(this);
            foreach (var binding in _owner.Live())
            {
                if (binding.Pads.TryGetValue(this, out var resource) && !resource.IsDestroyed)
                {
                    resource.SendRemoved();
                }
            }
        }

        /// <summary>
        /// Gives the pad focus on a surface and sends <c>enter</c> with the tablet that the pad belongs to.
        /// </summary>
        /// <param name="tablet">The tablet that the pad belongs to.</param>
        /// <param name="surface">The surface that gets the pad focus.</param>
        public void NotifyEnter(TabletDevice tablet, Surface surface)
        {
            _surface = surface;
            ForCurrent((resource, binding) =>
            {
                if (binding.Tablets.TryGetValue(tablet, out var tabletResource))
                {
                    resource.SendEnter(_owner._display.NextSerial(), tabletResource, surface.Resource);
                }
            });
        }

        /// <summary>
        /// Sends <c>leave</c> and clears the pad focus.
        /// </summary>
        public void NotifyLeave()
        {
            if (_surface is { } surface)
            {
                ForCurrent((resource, _) => resource.SendLeave(_owner._display.NextSerial(), surface.Resource));
            }

            _surface = null;
        }

        /// <summary>
        /// Sends a pad button event.
        /// </summary>
        /// <param name="timeMs">The event time in milliseconds.</param>
        /// <param name="button">The zero-based pad button index.</param>
        /// <param name="pressed"><see langword="true"/> for a press, <see langword="false"/> for a release.</param>
        public void NotifyButton(uint timeMs, uint button, bool pressed) =>
            ForCurrent((resource, _) => resource.SendButton(
                timeMs,
                button,
                pressed ? ZwpTabletPadV2.ButtonState.Pressed : ZwpTabletPadV2.ButtonState.Released));

        /// <summary>
        /// Sends a dial rotation and a dial frame. A zero value or an unknown dial sends nothing.
        /// </summary>
        /// <param name="timeMs">The event time in milliseconds.</param>
        /// <param name="dial">The zero-based dial index.</param>
        /// <param name="value120">The rotation in 120ths of a detent.</param>
        public void NotifyDial(uint timeMs, uint dial, int value120)
        {
            if (value120 == 0)
            {
                return;
            }

            ForCurrent((_, binding) =>
            {
                if (binding.PadDials.TryGetValue(this, out var dials) &&
                    dial < dials.Length &&
                    !dials[dial].IsDestroyed)
                {
                    dials[dial].SendDelta(value120);
                    dials[dial].SendFrame(timeMs);
                }
            });
        }

        private void ForCurrent(Action<ZwpTabletPadV2Resource, SeatBinding> send)
        {
            if (_surface is null or { IsDestroyed: true })
            {
                return;
            }

            var client = _surface.Resource.Client;
            foreach (var binding in _owner.Live())
            {
                if (binding.Resource.Client == client &&
                    binding.Pads.TryGetValue(this, out var resource) &&
                    !resource.IsDestroyed)
                {
                    send(resource, binding);
                }
            }
        }
    }
}
