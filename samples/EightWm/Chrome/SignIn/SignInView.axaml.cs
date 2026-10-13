using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace EightWm;

public sealed partial class SignInView : UserControl
{
    private const double Gutter = 24;
    private const double RowFallback = 32;

    private SignInModel? _model;

    public SignInView()
    {
        InitializeComponent();
        var reveal = RevealButton;
        reveal.AddHandler(PointerPressedEvent, (_, _) => SetRevealed(true), RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        reveal.AddHandler(PointerReleasedEvent, (_, _) => SetRevealed(false), RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        reveal.AddHandler(PointerCaptureLostEvent, (_, _) => SetRevealed(false), RoutingStrategies.Bubble, handledEventsToo: true);
        PasswordBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && _model is { } model)
            {
                model.Submit.Execute(null);
                e.Handled = true;
            }
        };
        LayoutUpdated += (_, _) => FitOptions();
        var options = OptionsReveal;
        options.OpenDuration = TimeSpan.FromMilliseconds(367);
        options.CloseDuration = TimeSpan.FromMilliseconds(367);
        options.CloseDelay = TimeSpan.FromMilliseconds(167);
        options.FadeDuration = TimeSpan.FromMilliseconds(167);
        options.FadeOpenDelay = TimeSpan.FromMilliseconds(200);
        options.Easing = AvaWin.Animations.WinEasing.Standard;
    }

    internal bool OptionsMoving => OptionsReveal.IsSliding;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_model is not null)
        {
            _model.FocusPassword -= OnFocusPassword;
            _model.PropertyChanged -= OnModelChanged;
        }

        _model = DataContext as SignInModel;
        if (_model is not null)
        {
            _model.FocusPassword += OnFocusPassword;
            _model.PropertyChanged += OnModelChanged;
            if (_model.OptionsOpen)
            {
                Dispatcher.UIThread.Post(ShowChosen, DispatcherPriority.Loaded);
            }
        }
    }

    private void OnModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SignInModel.OptionsOpen) && _model is { OptionsOpen: true })
        {
            Dispatcher.UIThread.Post(ShowChosen, DispatcherPriority.Loaded);
        }
    }

    private void ShowChosen()
    {
        foreach (var list in new[] { SessionList, LayoutList })
        {
            foreach (var option in list.GetVisualDescendants().OfType<RadioButton>())
            {
                if (option.IsChecked == true)
                {
                    option.BringIntoView();
                }
            }
        }
    }

    private void FitOptions()
    {
        if (!OptionsPanel.IsVisible || Bounds.Height <= 0)
        {
            return;
        }

        var row = SessionList.GetVisualDescendants().OfType<RadioButton>().FirstOrDefault()?.Bounds.Height is > 0 and var height ? height : RowFallback;
        var rest = Box.DesiredSize.Height - OptionsReveal.DesiredSize.Height + OptionsPanel.DesiredSize.Height
            - SessionScroller.DesiredSize.Height - LayoutScroller.DesiredSize.Height;
        var spare = Math.Max(0, Bounds.Height - (2 * Gutter) - rest);
        var layouts = Math.Min(LayoutList.DesiredSize.Height, Math.Max(2 * row, Math.Floor(spare * 0.4)));
        var sessions = Math.Max(2 * row, Math.Floor(spare - layouts));
        Limit(SessionScroller, sessions);
        Limit(LayoutScroller, Math.Max(2 * row, layouts));
    }

    private static void Limit(ScrollViewer scroller, double height)
    {
        if (Math.Abs(scroller.MaxHeight - height) >= 1)
        {
            scroller.MaxHeight = height;
        }
    }

    private void SetRevealed(bool on)
    {
        if (_model is { } model)
        {
            model.Revealed = on;
        }
    }

    private void OnFocusPassword() => Dispatcher.UIThread.Post(() => PasswordBox.Focus(NavigationMethod.Tab), DispatcherPriority.Input);
}
