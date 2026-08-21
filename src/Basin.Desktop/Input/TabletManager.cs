using Basin.Capabilities;
using Basin.Desktop.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Desktop;

public sealed class TabletManager : ITabletObserver, IDisposable
{
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

    public TabletTool? ToolFor(ulong id) => _sourceTools.GetValueOrDefault(id);

    public TabletDevice? TabletFor(ulong id) => _sourceTablets.GetValueOrDefault(id);

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

    public event Action<TabletTool, TabletDevice, TabletToolAxes>? ToolProximityIn;

    public event Action<TabletTool, TabletToolAxes>? ToolMoved;

    public event Action<TabletPad, TabletPadEvent>? PadActivity;

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

    public void OnToolAxis(ulong toolId, uint timeMs, TabletToolAxes axes)
    {
        if (_sourceTools.GetValueOrDefault(toolId) is not { } tool)
        {
            return;
        }

        _toolAxes[toolId] = axes;
        tool.LastTimeMs = timeMs;
        ToolMoved?.Invoke(tool, axes);
        tool.NotifyAxes(axes);
        tool.NotifyFrame(timeMs);
    }

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

    [Flags]
    public enum TabletToolCapabilities : uint
    {
        Tilt = 1 << 0,
        Pressure = 1 << 1,
        Distance = 1 << 2,
        Rotation = 1 << 3,
        Slider = 1 << 4,
        Wheel = 1 << 5,
    }

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

        public string Name { get; }

        public uint VendorId { get; }

        public uint ProductId { get; }

        public string Path { get; }

        public uint BusType { get; }

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

        public ZwpTabletToolV2.Type Type { get; }

        public ulong HardwareSerial { get; }

        public TabletToolCapabilities Capabilities { get; }

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

        public Surface? Focus => _surface;

        public void NotifyProximityIn(TabletDevice tablet, Surface surface, double x, double y)
        {
            Tablet = tablet;
            SetFocus(surface, x, y);
        }

        public void SetFocus(Surface? surface, double x, double y)
        {
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

        public uint LastTimeMs { get; internal set; }

        public void NotifyProximityOut()
        {
            ForCurrent((resource, _) => resource.SendProximityOut());
            _leaving = true;
            Tablet = null;
        }

        public void NotifyDown() =>
            ForCurrent((resource, _) => resource.SendDown(_owner._display.NextSerial()));

        public void NotifyUp() => ForCurrent((resource, _) => resource.SendUp());

        public void NotifyMotion(double x, double y) =>
            ForCurrent((resource, _) => resource.SendMotion(WlFixed.FromDouble(x), WlFixed.FromDouble(y)));

        public void NotifyPressure(double pressure) =>
            ForCurrent((resource, _) => resource.SendPressure((uint)Math.Clamp(pressure * 65535, 0, 65535)));

        public void NotifyTilt(double tiltX, double tiltY) =>
            ForCurrent((resource, _) => resource.SendTilt(WlFixed.FromDouble(tiltX), WlFixed.FromDouble(tiltY)));

        public void NotifyDistance(double distance) =>
            ForCurrent((resource, _) => resource.SendDistance((uint)Math.Clamp(distance * 65535, 0, 65535)));

        public void NotifyRotation(double degrees) =>
            ForCurrent((resource, _) => resource.SendRotation(WlFixed.FromDouble(degrees)));

        public void NotifyButton(uint button, bool pressed) =>
            ForCurrent((resource, _) => resource.SendButton(
                _owner._display.NextSerial(),
                button,
                pressed ? ZwpTabletToolV2.ButtonState.Pressed : ZwpTabletToolV2.ButtonState.Released));

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

        public string Path { get; }

        public uint Buttons { get; }

        public uint Dials { get; }

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

        public void NotifyLeave()
        {
            if (_surface is { } surface)
            {
                ForCurrent((resource, _) => resource.SendLeave(_owner._display.NextSerial(), surface.Resource));
            }

            _surface = null;
        }

        public void NotifyButton(uint timeMs, uint button, bool pressed) =>
            ForCurrent((resource, _) => resource.SendButton(
                timeMs,
                button,
                pressed ? ZwpTabletPadV2.ButtonState.Pressed : ZwpTabletPadV2.ButtonState.Released));

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
