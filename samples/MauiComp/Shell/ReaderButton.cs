using Basin.UI.Avalonia;

namespace MauiComp.Shell;

internal sealed class ReaderButton : Behavior<View>
{
    public static readonly BindableProperty LabelProperty =
        BindableProperty.Create(nameof(Label), typeof(string), typeof(ReaderButton), propertyChanged: (bindable, _, _) => ((ReaderButton)bindable).Apply());

    private View? _view;
    private global::Avalonia.Interactivity.Interactive? _control;

    public event EventHandler? Invoked;

    public string? Label
    {
        get => (string?)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    protected override void OnAttachedTo(View bindable)
    {
        base.OnAttachedTo(bindable);
        _view = bindable;
        BindingContext = bindable.BindingContext;
        bindable.BindingContextChanged += OnContextChanged;
        bindable.HandlerChanged += OnHandlerChanged;
        Apply();
    }

    protected override void OnDetachingFrom(View bindable)
    {
        bindable.BindingContextChanged -= OnContextChanged;
        bindable.HandlerChanged -= OnHandlerChanged;
        Release();
        _view = null;
        base.OnDetachingFrom(bindable);
    }

    private void OnContextChanged(object? sender, EventArgs e) => BindingContext = _view?.BindingContext;

    private void OnHandlerChanged(object? sender, EventArgs e) => Apply();

    private void Apply()
    {
        if (_view?.Handler?.PlatformView is not global::Avalonia.Interactivity.Interactive control)
        {
            return;
        }

        if (!ReferenceEquals(control, _control))
        {
            Release();
            _control = control;
            ChromeAutomation.SetIsButton(control, true);
            ChromeAutomation.AddInvokedHandler(control, OnInvoked);
        }

        global::Avalonia.Automation.AutomationProperties.SetName(control, Label);
    }

    private void Release()
    {
        if (_control is { } control)
        {
            ChromeAutomation.RemoveInvokedHandler(control, OnInvoked);
            _control = null;
        }
    }

    private void OnInvoked(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_view is null)
        {
            return;
        }

        foreach (var tap in _view.GestureRecognizers.OfType<TapGestureRecognizer>())
        {
            if (tap.Command is { } command && command.CanExecute(tap.CommandParameter))
            {
                command.Execute(tap.CommandParameter);
            }
        }

        Invoked?.Invoke(this, EventArgs.Empty);
    }
}
