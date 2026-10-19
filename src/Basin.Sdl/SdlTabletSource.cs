using Basin.Capabilities;
using SDL3;

namespace Basin.Sdl;

public sealed class SdlTabletSource : ITabletSource, ISdlEventSink
{
    public const uint BtnTouch = 0x14a;
    public const uint BtnStylus = 0x14b;
    public const uint BtnStylus2 = 0x14c;
    public const uint BtnStylus3 = 0x149;

    private const TabletToolAxis PenAxes = TabletToolAxis.Pressure | TabletToolAxis.Tilt | TabletToolAxis.Distance |
        TabletToolAxis.Rotation | TabletToolAxis.Slider;

    private readonly List<TabletInfo> _tablets = [];
    private readonly List<TabletToolInfo> _tools = [];
    private readonly Dictionary<uint, Pen> _pens = [];
    private readonly TabletObservers _observers = new();
    private ulong _nextId = 1;

    private sealed class Pen(TabletInfo tablet, TabletToolInfo tip, TabletToolInfo eraser)
    {
        public TabletInfo Tablet { get; } = tablet;

        public TabletToolInfo Tip { get; } = tip;

        public TabletToolInfo Eraser { get; } = eraser;

        public bool UsingEraser;

        public bool InProximity;

        public bool Located;

        public TabletToolAxes Axes;

        public ulong ToolId => UsingEraser ? Eraser.Id : Tip.Id;
    }

    public event Action<TabletInfo>? TabletAdded;

    public event Action<ulong>? TabletRemoved
    {
        add { }
        remove { }
    }

    public event Action<TabletToolInfo>? ToolAdded;

    public event Action<ulong>? ToolRemoved
    {
        add { }
        remove { }
    }

    public event Action<TabletPadInfo>? PadAdded
    {
        add { }
        remove { }
    }

    public event Action<ulong>? PadRemoved
    {
        add { }
        remove { }
    }

    public Func<uint, float, float, (double X, double Y)?>? Locate { get; set; }

    public void AddObserver(ITabletObserver observer) => _observers.Add(observer);

    public void RemoveObserver(ITabletObserver observer) => _observers.Remove(observer);

    public int EnumerateTablets(Span<TabletInfo> tablets) => Copy(_tablets, tablets);

    public int EnumerateTools(Span<TabletToolInfo> tools) => Copy(_tools, tools);

    public int EnumeratePads(Span<TabletPadInfo> pads) => 0;

    public bool Handle(in SDL.Event e)
    {
        var type = (SDL.EventType)e.Type;
        if (type is < SDL.EventType.PenFirst or > SDL.EventType.PenLast)
        {
            return false;
        }

        var time = (uint)(e.PMotion.Timestamp / 1_000_000UL);
        switch (type)
        {
            case SDL.EventType.PenProximityIn:
                PenFor(e.PProximity.Which);
                return true;
            case SDL.EventType.PenProximityOut:
                if (_pens.TryGetValue(e.PProximity.Which, out var leaving))
                {
                    leaving.Located = false;
                    if (leaving.InProximity)
                    {
                        leaving.InProximity = false;
                        _observers.ToolProximity(leaving.ToolId, leaving.Tablet.Id, false);
                    }
                }

                return true;
            case SDL.EventType.PenMotion:
                Move(e.PMotion.Which, e.PMotion.WindowID, e.PMotion.X, e.PMotion.Y, time);
                return true;
            case SDL.EventType.PenDown:
            case SDL.EventType.PenUp:
                var pen = PenFor(e.PTouch.Which);
                var down = type == SDL.EventType.PenDown;
                if (down)
                {
                    Switch(pen, e.PTouch.Eraser);
                }

                if (Enter(pen, time))
                {
                    _observers.ToolButton(pen.ToolId, time, BtnTouch, down);
                }

                return true;
            case SDL.EventType.PenButtonDown:
            case SDL.EventType.PenButtonUp:
                var pressed = PenFor(e.PButton.Which);
                if (Enter(pressed, time) && ButtonCode(e.PButton.Button) is var button and not 0)
                {
                    _observers.ToolButton(pressed.ToolId, time, button, type == SDL.EventType.PenButtonDown);
                }

                return true;
            case SDL.EventType.PenAxis:
                var axis = PenFor(e.PAxis.Which);
                axis.Axes = Apply(axis.Axes, e.PAxis.Axis, e.PAxis.Value);
                Update(axis, time);
                return true;
            default:
                return false;
        }
    }

    private bool Enter(Pen pen, uint time)
    {
        if (pen.InProximity)
        {
            return true;
        }

        if (!pen.Located)
        {
            return false;
        }

        pen.InProximity = true;
        _observers.ToolAxis(pen.ToolId, time, pen.Axes);
        _observers.ToolProximity(pen.ToolId, pen.Tablet.Id, true);
        return true;
    }

    private void Update(Pen pen, uint time)
    {
        if (pen.InProximity)
        {
            _observers.ToolAxis(pen.ToolId, time, pen.Axes);
        }
        else
        {
            Enter(pen, time);
        }
    }

    private void Switch(Pen pen, bool eraser)
    {
        if (pen.UsingEraser == eraser)
        {
            return;
        }

        if (pen.InProximity)
        {
            _observers.ToolProximity(pen.ToolId, pen.Tablet.Id, false);
            pen.InProximity = false;
        }

        pen.UsingEraser = eraser;
    }

    private void Move(uint which, uint window, float x, float y, uint time)
    {
        var pen = PenFor(which);
        if ((Locate ?? LocateInWindow)(window, x, y) is not { } at)
        {
            return;
        }

        pen.Axes = pen.Axes with { X = at.X, Y = at.Y };
        pen.Located = true;
        Update(pen, time);
    }

    private static (double X, double Y)? LocateInWindow(uint windowId, float x, float y)
    {
        var window = SDL.GetWindowFromID(windowId);
        if (window == 0 || !SDL.GetWindowSize(window, out var width, out var height) || width <= 0 || height <= 0)
        {
            return null;
        }

        return (x / width, y / height);
    }

    public static TabletToolAxes Apply(TabletToolAxes axes, SDL.PenAxis axis, float value) => axis switch
    {
        SDL.PenAxis.Pressure => axes with { Pressure = Math.Clamp(value, 0f, 1f) },
        SDL.PenAxis.XTilt => axes with { TiltX = value },
        SDL.PenAxis.YTilt => axes with { TiltY = value },
        SDL.PenAxis.Distance => axes with { Distance = Math.Clamp(value, 0f, 1f) },
        SDL.PenAxis.Rotation => axes with { Rotation = value },
        SDL.PenAxis.Slider => axes with { Slider = Math.Clamp(value, 0f, 1f) },
        _ => axes,
    };

    public static uint ButtonCode(byte button) => button switch
    {
        1 => BtnStylus,
        2 => BtnStylus2,
        3 => BtnStylus3,
        _ => 0,
    };

    private Pen PenFor(uint which)
    {
        if (_pens.TryGetValue(which, out var pen))
        {
            return pen;
        }

        var tablet = new TabletInfo(_nextId++, $"SDL pen {which}", 0, 0, $"sdl:pen:{which}");
        var tip = new TabletToolInfo(_nextId++, TabletToolType.Pen, which, PenAxes);
        var eraser = new TabletToolInfo(_nextId++, TabletToolType.Eraser, which, PenAxes);
        pen = new Pen(tablet, tip, eraser);
        _pens[which] = pen;
        _tablets.Add(tablet);
        TabletAdded?.Invoke(tablet);
        _tools.Add(tip);
        ToolAdded?.Invoke(tip);
        _tools.Add(eraser);
        ToolAdded?.Invoke(eraser);
        return pen;
    }

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
