using Basin;
using Basin.Diagnostics;
using Basin.Scene;
using Basin.SoftKeyboard;
using BasinGreeter.UI;

namespace BasinGreeter;

internal sealed class GreeterKeyboard : IDisposable
{
    private readonly SoftKeyboardEngine _engine;
    private readonly KeyboardRouter _router;
    private readonly SoftKeyboardTextInput _textInput = new();
    private readonly KeyboardSize _size;
    private readonly SoftKeyboardTyper _typer;
    private readonly SoftKeyboardTimer _timer;

    public GreeterKeyboard(ICompositorEventLoop loop, UISurfaceRouter router, Basin.Seat.SeatKeyboard keyboard, string size, BasinLogger log)
    {
        var locale = Environment.GetEnvironmentVariable("LC_ALL") is { Length: > 0 } all ? all : Environment.GetEnvironmentVariable("LANG");
        var layout = KeyboardLayouts.Load(KeyboardLayouts.DefaultFor(locale), log: log) ?? KeyboardLayouts.Load("us-qwerty")!;
        _engine = new SoftKeyboardEngine(layout.Grid());
        _router = new KeyboardRouter(_textInput, new UISurfaceKeyOutput(router, keyboard)) { SpacesToPeriod = false };
        _typer = new SoftKeyboardTyper(_engine);
        _router.Attach(_engine, () => Now);
        _timer = new SoftKeyboardTimer(loop, _engine, () => Now);
        _engine.HideRequested += () => HideRequested?.Invoke();
        _size = size switch
        {
            "xs" => KeyboardSize.ExtraSmall,
            "s" => KeyboardSize.Small,
            "l" => KeyboardSize.Large,
            _ => KeyboardSize.Medium,
        };
        LayoutId = layout.Id;
    }

    public SoftKeyboardEngine Engine => _engine;

    public string LayoutId { get; }

    public event Action? HideRequested;

    public long Now => _typer.Clock;

    public int HeightFor(int outputWidth, int outputHeight) => KeyboardMetrics.For(_size, outputWidth, outputHeight).Height;

    public void Layout(int width, int outputHeight)
    {
        var metrics = KeyboardMetrics.ForHeight(KeyboardMetrics.PresetHeight(_size), outputHeight);
        _engine.Layout(width, metrics.KeymapHeight, metrics.Padding);
    }

    public bool TypeText(string text) => _typer.Type(text);

    public bool Tap(string name) => _typer.Tap(name);

    public void Dispose() => _timer.Dispose();
}
