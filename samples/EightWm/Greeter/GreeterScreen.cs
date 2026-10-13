using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Basin.SoftKeyboard.Avalonia;

namespace EightWm;

public sealed class GreeterScreen : Grid
{
    public GreeterScreen(SignInModel signIn, CurtainModel curtain, NetworkModel network)
    {
        SignIn = new SignInView { DataContext = signIn };
        Curtain = new LockCurtainView { DataContext = curtain };
        Network = new NetworkFlyoutView
        {
            DataContext = network,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(80, 0, 0, 360),
        };
        Keyboard = new SoftKeyboardCanvas
        {
            Input = SoftKeyboardInput.Pointer,
        };
        KeyboardPanel = new SoftKeyboardPanel
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            OpenDuration = TimeSpan.FromMilliseconds(AnimationCatalog.Of(Animation.ShowPanel).DurationMs),
            CloseDuration = TimeSpan.FromMilliseconds(AnimationCatalog.Of(Animation.HidePanel).DurationMs),
            Easing = new CurveEasing(AnimationCurve.Deceleration),
            Child = Keyboard,
        };
        KeyboardPanel.PropertyChanged += (_, e) =>
        {
            if (e.Property == RevealPanel.RevealedProperty)
            {
                SignIn.Margin = new Thickness(0, 0, 0, KeyboardPanel.Revealed);
            }
        };
        Children.Add(SignIn);
        Children.Add(KeyboardPanel);
        Children.Add(Curtain);
        Children.Add(Network);
    }

    public SignInView SignIn { get; }

    public LockCurtainView Curtain { get; }

    public NetworkFlyoutView Network { get; }

    public SoftKeyboardCanvas Keyboard { get; }

    public SoftKeyboardPanel KeyboardPanel { get; }
}
