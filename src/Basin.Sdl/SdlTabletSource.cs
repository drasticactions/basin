using Basin.Capabilities;
using SDL3;

namespace Basin.Sdl;

/// <summary>
/// An <see cref="ITabletSource"/> over SDL pen events. Each SDL pen becomes one tablet with a pen tool and an eraser tool.
/// SDL reports the eraser only at contact, so a hovering eraser shows as the pen tool.
/// </summary>
public sealed class SdlTabletSource : ITabletSource, ISdlEventSink
{
    /// <summary>
    /// The evdev code <c>BTN_TOUCH</c>, which a pen contact sends.
    /// </summary>
    public const uint BtnTouch = 0x14a;
    /// <summary>
    /// The evdev code <c>BTN_STYLUS</c>, for SDL pen button 1.
    /// </summary>
    public const uint BtnStylus = 0x14b;
    /// <summary>
    /// The evdev code <c>BTN_STYLUS2</c>, for SDL pen button 2.
    /// </summary>
    public const uint BtnStylus2 = 0x14c;
    /// <summary>
    /// The evdev code <c>BTN_STYLUS3</c>, for SDL pen button 3.
    /// </summary>
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

    /// <summary>
    /// Occurs when the source sees an SDL pen for the first time, before that pen adds its tools.
    /// </summary>
    public event Action<TabletInfo>? TabletAdded;

    /// <summary>
    /// Never occurs. The source keeps every pen that it saw.
    /// </summary>
    public event Action<ulong>? TabletRemoved
    {
        add { }
        remove { }
    }

    /// <summary>
    /// Occurs twice for each new SDL pen, for the pen tool and then the eraser tool.
    /// </summary>
    public event Action<TabletToolInfo>? ToolAdded;

    /// <summary>
    /// Never occurs. The source keeps every tool that it made.
    /// </summary>
    public event Action<ulong>? ToolRemoved
    {
        add { }
        remove { }
    }

    /// <summary>
    /// Never occurs. SDL reports no tablet pads.
    /// </summary>
    public event Action<TabletPadInfo>? PadAdded
    {
        add { }
        remove { }
    }

    /// <summary>
    /// Never occurs. SDL reports no tablet pads.
    /// </summary>
    public event Action<ulong>? PadRemoved
    {
        add { }
        remove { }
    }

    /// <summary>
    /// Gets or sets the function that turns an SDL window id and a position in points into a position normalized from 0 to 1.
    /// It returns <see langword="null"/> to ignore the motion. When it is <see langword="null"/>, the position is normalized over its own window.
    /// </summary>
    public Func<uint, float, float, (double X, double Y)?>? Locate { get; set; }

    /// <inheritdoc/>
    public void AddObserver(ITabletObserver observer) => _observers.Add(observer);

    /// <inheritdoc/>
    public void RemoveObserver(ITabletObserver observer) => _observers.Remove(observer);

    /// <inheritdoc/>
    public int EnumerateTablets(Span<TabletInfo> tablets) => Copy(_tablets, tablets);

    /// <inheritdoc/>
    public int EnumerateTools(Span<TabletToolInfo> tools) => Copy(_tools, tools);

    /// <summary>
    /// Copies no pads, because SDL reports no tablet pads.
    /// </summary>
    /// <param name="pads">The span that receives the pads. It is not written.</param>
    /// <returns>Always 0.</returns>
    public int EnumeratePads(Span<TabletPadInfo> pads) => 0;

    /// <summary>
    /// Turns SDL pen events into tool events for the observers. A tool enters proximity at the first motion that <see cref="Locate"/> places.
    /// </summary>
    /// <param name="e">The SDL event.</param>
    /// <returns><see langword="true"/> for a pen event, else <see langword="false"/>.</returns>
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

    /// <summary>
    /// Sets one SDL pen axis on a set of tool axes. Pressure, distance and slider are clamped to 0 to 1. Tilt and rotation are degrees.
    /// </summary>
    /// <param name="axes">The axes before the change.</param>
    /// <param name="axis">The SDL pen axis. An axis with no tablet slot leaves the axes unchanged.</param>
    /// <param name="value">The SDL axis value.</param>
    /// <returns>The axes with the new value.</returns>
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

    /// <summary>
    /// Maps an SDL pen button to an evdev stylus button code.
    /// </summary>
    /// <param name="button">The SDL pen button number.</param>
    /// <returns><see cref="BtnStylus"/>, <see cref="BtnStylus2"/> or <see cref="BtnStylus3"/> for buttons 1 to 3, else 0.</returns>
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
