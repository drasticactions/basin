using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace LunaWm;

public sealed partial class GreeterLoginView : UserControl
{
    private readonly DispatcherTimer _shake = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private GreeterLoginModel? _model;
    private int _shakeFrame;
    private int _shakes;

    public Basin.SoftKeyboard.Avalonia.SoftKeyboardPanel KeyboardPanel => this.FindControl<Basin.SoftKeyboard.Avalonia.SoftKeyboardPanel>("KeyboardHost")!;

    public GreeterLoginView()
    {
        InitializeComponent();
        var panel = KeyboardPanel;
        panel.OpenDuration = TimeSpan.FromMilliseconds(LunaMotion.PositiveSpace.DurationMs);
        panel.CloseDuration = TimeSpan.FromMilliseconds(LunaMotion.PositiveSpace.DurationMs);
        panel.Easing = new QtCurveEasing(LunaMotion.PositiveSpace.Curve);
        _shake.Tick += (_, _) => Shake();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_model is not null)
        {
            _model.FocusRequested -= OnFocusRequested;
            _model.PropertyChanged -= OnModelChanged;
        }

        _model = DataContext as GreeterLoginModel;
        if (_model is not null)
        {
            _shakes = _model.Shakes;
            _model.FocusRequested += OnFocusRequested;
            _model.PropertyChanged += OnModelChanged;
        }
    }

    internal KeyboardView KeyboardView => Keyboard;

    internal void FocusCurrent()
    {
        if (_model is null)
        {
            return;
        }

        Control? target = _model.ShowUsers
            ? SelectedAvatar()
            : _model.NeedsDismiss ? OkButton
            : _model.AskName ? NameBox : _model.VisibleShown ? VisibleField : Field;
        target?.Focus(NavigationMethod.Tab);
    }

    private Control? SelectedAvatar()
    {
        if (_model?.SelectedUser is not { } user || UserStrip.ContainerFromItem(user) is not { } container)
        {
            return null;
        }

        return container as Button ?? Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(container).OfType<Button>().FirstOrDefault();
    }

    internal void RequestFocus() => Dispatcher.UIThread.Post(FocusCurrent, DispatcherPriority.ContextIdle);

    private void OnFocusRequested() => RequestFocus();

    private void OnModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GreeterLoginModel.Shakes) && _model is { } model && model.Shakes != _shakes)
        {
            _shakes = model.Shakes;
            _shakeFrame = 0;
            _shake.Start();
        }
    }

    private void Shake()
    {
        var shift = (TranslateTransform)Pad.RenderTransform!;
        _shakeFrame++;
        var t = _shakeFrame / 24.0;
        shift.X = t >= 1 ? 0 : Math.Sin(t * Math.PI * 6) * 14 * (1 - t);
        if (t >= 1)
        {
            _shake.Stop();
        }
    }
}
