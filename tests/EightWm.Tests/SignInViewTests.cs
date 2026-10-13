using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basin.UI.Avalonia;
using Xunit;

namespace EightWm.Tests;

public sealed class SignInViewTests
{
    private static readonly Color Accent = Color.FromRgb(0x46, 0x17, 0x9b);

    private static Window Host(Control content, double width = 1366, double height = 768)
    {
        SignInTheme.Install(Application.Current!, Accent);
        var window = new Window { Content = content, Width = width, Height = height };
        window.Show();
        Pump();
        window.UpdateLayout();
        return window;
    }

    private static void Pump()
    {
        for (var i = 0; i < 4; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    private static void Wait(Func<bool> condition, int timeoutMs = 3000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (condition() && Environment.TickCount64 < deadline)
        {
            var frame = new DispatcherFrame();
            DispatcherTimer.RunOnce(() => frame.Continue = false, TimeSpan.FromMilliseconds(10));
            Dispatcher.UIThread.PushFrame(frame);
            Pump();
        }
    }

    private static void For(int milliseconds) => Wait(() => true, milliseconds);

    private static Point Center(Control control, Visual root) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), root)!.Value;

    [AvaloniaFact]
    public void A_pressed_user_tile_keeps_no_fill_and_shrinks_as_a_windows_8_tile_does()
    {
        var model = TwoUsers();
        model.ShowUsers(null);
        var view = new SignInView { DataContext = model };
        var window = Host(view);
        var tile = view.GetVisualDescendants().OfType<Button>().First(button => button.Classes.Contains("userTile"));
        var presenter = tile.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().First(part => part.Name == "PART_ContentPresenter");

        window.MouseMove(Center(tile, window));
        Pump();
        Assert.True(presenter.Background is null or ISolidColorBrush { Color.A: 0 }, $"hover fills the tile with {presenter.Background}");

        window.MouseDown(Center(tile, window), MouseButton.Left);
        Pump();
        Assert.True(tile.IsPressed);
        Assert.True(presenter.Background is null or ISolidColorBrush { Color.A: 0 }, $"a press fills the tile with {presenter.Background}");
        var scale = Assert.IsType<Avalonia.Media.Transformation.TransformOperations>(tile.RenderTransform).Value;
        Assert.Equal(0.975, scale.M11, 3);

        window.MouseUp(Center(tile, window), MouseButton.Left);
        Pump();
        window.Close();
    }

    [AvaloniaFact]
    public void A_long_session_list_scrolls_inside_the_screen()
    {
        var model = TwoUsers();
        model.Sessions.Clear();
        for (var i = 0; i < 40; i++)
        {
            model.Sessions.Add(new SignInOption($"session-{i}", $"Session {i}") { Chosen = i == 39 });
        }

        model.BeginConversation(model.Users[0]);
        model.ShowPrompt("Password", secret: true);
        model.ToggleOptions.Execute(null);
        var view = new SignInView { DataContext = model };
        var window = Host(view);
        var panel = view.FindControl<Border>("OptionsPanel")!;
        Assert.True(panel.IsEffectivelyVisible);
        var top = panel.TranslatePoint(default, window)!.Value.Y;
        var box = view.FindControl<StackPanel>("Box")!;
        var boxTop = box.TranslatePoint(default, window)!.Value.Y;
        Assert.True(boxTop >= 0, $"the sign-in box starts above the screen at {boxTop}");
        Assert.True(top + panel.Bounds.Height <= 768, $"the options panel ends at {top + panel.Bounds.Height}, below the 768 screen");

        var list = view.FindControl<ItemsControl>("SessionList")!;
        var scroller = list.FindAncestorOfType<ScrollViewer>();
        Assert.NotNull(scroller);
        Assert.True(scroller!.Extent.Height > scroller.Viewport.Height);
        var chosen = list.GetVisualDescendants().OfType<RadioButton>().Last();
        var y = chosen.TranslatePoint(default, scroller)!.Value.Y;
        Assert.InRange(y, 0, scroller.Viewport.Height - chosen.Bounds.Height);

        var tall = scroller.Bounds.Height;
        window.Height = 560;
        for (var i = 0; i < 3; i++)
        {
            Pump();
            window.UpdateLayout();
        }

        top = panel.TranslatePoint(default, window)!.Value.Y;
        boxTop = box.TranslatePoint(default, window)!.Value.Y;
        Assert.True(boxTop >= 0, $"in a shorter view the sign-in box starts above it at {boxTop}");
        Assert.True(top + panel.Bounds.Height <= 560, $"in a shorter view the options panel ends at {top + panel.Bounds.Height}");
        Assert.True(scroller.Bounds.Height < tall);
        window.Close();
    }

    [AvaloniaFact]
    public void A_section_with_no_options_is_hidden()
    {
        var model = TwoUsers();
        model.Layouts.Clear();
        model.BeginConversation(model.Users[0]);
        model.ShowPrompt("Password", secret: true);
        model.ToggleOptions.Execute(null);
        var view = new SignInView { DataContext = model };
        var window = Host(view);
        var headers = view.FindControl<Border>("OptionsPanel")!.GetVisualDescendants().OfType<TextBlock>().ToList();
        Assert.Contains(headers, block => block.Text == "Session" && block.IsEffectivelyVisible);
        Assert.DoesNotContain(headers, block => block.Text == "Keyboard layout" && block.IsEffectivelyVisible);
        Assert.False(view.FindControl<ScrollViewer>("LayoutScroller")!.IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public void The_options_expand_before_they_fade_in_and_fade_out_before_they_collapse()
    {
        var model = TwoUsers();
        model.BeginConversation(model.Users[0]);
        model.ShowPrompt("Password", secret: true);
        var view = new SignInView { DataContext = model };
        var window = Host(view);
        Assert.Equal(0, view.OptionsReveal.Revealed);

        model.ToggleOptions.Execute(null);
        Assert.True(view.OptionsMoving);
        For(120);
        window.UpdateLayout();
        Assert.InRange(view.OptionsReveal.Revealed, 1, view.OptionsPanel.DesiredSize.Height - 1);
        Assert.Equal(0, view.OptionsPanel.Opacity);

        Wait(() => view.OptionsMoving);
        window.UpdateLayout();
        var full = view.OptionsPanel.DesiredSize.Height;
        Assert.Equal(full, view.OptionsReveal.Revealed);
        Assert.Equal(1, view.OptionsPanel.Opacity);

        model.ToggleOptions.Execute(null);
        For(100);
        window.UpdateLayout();
        Assert.Equal(full, view.OptionsReveal.Revealed);
        Assert.InRange(view.OptionsPanel.Opacity, 0, 0.99);

        Wait(() => view.OptionsMoving);
        window.UpdateLayout();
        Assert.Equal(0, view.OptionsReveal.Revealed);
        Assert.False(view.OptionsPanel.IsVisible);
        window.Close();
    }

    private static SignInModel TwoUsers()
    {
        var model = new SignInModel();
        model.Users.Add(new UserTileModel("ada", "Ada Lovelace", null));
        model.Users.Add(new UserTileModel("grace", "Grace Hopper", null));
        model.Sessions.Add(new SignInOption("tinycomp", "TinyComp") { Chosen = true });
        model.Sessions.Add(new SignInOption("sway", "Sway"));
        model.Layouts.Add(new SignInOption("us", "English (US)") { Chosen = true });
        model.Layouts.Add(new SignInOption("de", "German"));
        return model;
    }

    [AvaloniaFact]
    public void A_click_on_the_curtain_asks_to_raise_it_and_a_short_drag_springs_back()
    {
        var model = new CurtainModel { Clock = "9:41", Date = "Monday, October 12" };
        var asked = new List<string>();
        model.RaiseRequested += asked.Add;
        var view = new LockCurtainView { DataContext = model };
        var window = Host(view);

        window.MouseDown(new Point(600, 300), MouseButton.Left);
        window.MouseUp(new Point(602, 301), MouseButton.Left);
        Assert.Equal(["click"], asked);

        window.MouseDown(new Point(600, 400), MouseButton.Left);
        window.MouseMove(new Point(600, 350));
        Assert.Equal(-50, view.Offset, 1);
        window.MouseUp(new Point(600, 350), MouseButton.Left);
        Assert.Single(asked);

        window.MouseDown(new Point(600, 600), MouseButton.Left);
        window.MouseMove(new Point(600, 300));
        window.MouseUp(new Point(600, 300), MouseButton.Left);
        Assert.Equal(["click", "swipe"], asked);

        view.Focus();
        window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " ");
        Assert.Equal("key", asked[^1]);
    }

    [AvaloniaFact]
    public void A_raised_curtain_moves_off_the_top_and_stops_taking_input()
    {
        var model = new CurtainModel();
        var view = new LockCurtainView { DataContext = model };
        Host(view);
        model.Raised = true;
        Pump();
        Assert.False(view.IsHitTestVisible);
        Assert.Equal(TimeSpan.FromMilliseconds(550), LockCurtainView.RaiseDuration);
        model.Raised = false;
        Assert.True(view.IsHitTestVisible);
        Assert.Equal(0, view.Offset);
    }

    [AvaloniaFact]
    public void The_curtain_shows_the_clock_date_and_status_and_the_network_glyph_opens_the_flyout()
    {
        var model = new CurtainModel { Clock = "9:41", Date = "Monday, October 12", NetworkGlyph = "GlyphWifi", NetworkName = "Wi-Fi Home", BatteryText = "80%" };
        var opened = 0;
        model.NetworkRequested += () => opened++;
        var view = new LockCurtainView { DataContext = model };
        var window = Host(view);
        Assert.Equal("9:41", view.ClockText.Text);
        Assert.Equal(120, view.ClockText.FontSize);
        Assert.True(view.NetworkButton.IsVisible);
        Assert.NotNull(view.NetworkIcon.Data);
        Assert.True(view.BatteryIcon.IsVisible);
        var glyph = Center(view.NetworkButton, window);
        window.MouseDown(glyph, MouseButton.Left);
        window.MouseUp(glyph, MouseButton.Left);
        Assert.Equal(1, opened);
    }

    [AvaloniaFact]
    public void The_sign_in_box_shows_the_user_and_enter_submits_and_clears_the_password()
    {
        var model = TwoUsers();
        model.BeginConversation(model.Users[0]);
        var submitted = new List<string>();
        model.Submitted += submitted.Add;
        var view = new SignInView { DataContext = model };
        Host(view);
        Assert.Equal("Ada Lovelace", view.UserName.Text);
        Assert.True(view.Box.IsVisible);
        Assert.False(view.UserList.IsVisible);
        Assert.True(view.BackButton.IsVisible);
        model.Password = "secret";
        Pump();
        view.PasswordBox.Focus();
        view.PasswordBox.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Assert.Equal(["secret"], submitted);
        Assert.Equal("", model.Password);
        Assert.True(model.Busy);
        model.ShowError("The password is incorrect. Try again.");
        Pump();
        Assert.True(view.ErrorText.IsVisible);
        Assert.False(model.Busy);
    }

    [AvaloniaFact]
    public void Holding_the_eye_reveals_the_text_but_the_accessible_value_stays_masked()
    {
        var model = TwoUsers();
        model.BeginConversation(model.Users[0]);
        var view = new SignInView { DataContext = model };
        var window = Host(view);
        model.Password = "hunter2";
        Pump();
        var peer = (IValueProvider)ControlAutomationPeer.CreatePeerForElement(view.PasswordBox);
        Assert.Equal("●●●●●●●", peer.Value);

        var eye = Center(view.RevealButton, window);
        window.MouseDown(eye, MouseButton.Left);
        Pump();
        Assert.True(model.Revealed);
        Assert.True(view.PasswordBox.RevealPassword);
        Assert.Equal("●●●●●●●", peer.Value);
        window.MouseUp(eye, MouseButton.Left);
        Pump();
        Assert.False(model.Revealed);
        Assert.False(view.PasswordBox.RevealPassword);

        model.RevealAllowed = false;
        Pump();
        Assert.False(view.RevealButton.IsVisible);
        model.Revealed = true;
        Assert.False(model.Revealed);
    }

    [AvaloniaFact]
    public void The_back_arrow_shows_every_user_as_a_tile_and_a_tile_chooses_its_user()
    {
        var model = TwoUsers();
        model.BeginConversation(model.Users[1]);
        var back = 0;
        model.BackRequested += () =>
        {
            back++;
            model.ShowUsers(null);
        };
        var chosen = new List<string>();
        model.UserChosen += user => chosen.Add(user.Name);
        var view = new SignInView { DataContext = model };
        var window = Host(view);
        var arrow = Center(view.BackButton, window);
        window.MouseDown(arrow, MouseButton.Left);
        window.MouseUp(arrow, MouseButton.Left);
        Pump();
        Assert.Equal(1, back);
        Assert.True(view.UserList.IsVisible);
        Assert.False(view.Box.IsVisible);
        var tile = view.UserList.ContainerFromIndex(1)!;
        var point = Center(tile, window);
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Assert.Equal(["grace"], chosen);
        Assert.Equal("GH", model.Users[1].Initials);
    }

    [AvaloniaFact]
    public void Sign_in_options_list_the_sessions_and_layouts_and_a_choice_reaches_the_model()
    {
        var model = TwoUsers();
        model.BeginConversation(model.Users[0]);
        var sessions = new List<string>();
        var layouts = new List<string>();
        model.SessionChosen += sessions.Add;
        model.LayoutChosen += layouts.Add;
        var view = new SignInView { DataContext = model };
        Host(view);
        Assert.False(view.OptionsPanel.IsVisible);
        model.ToggleOptions.Execute(null);
        Pump();
        Assert.True(view.OptionsPanel.IsVisible);
        model.ChooseSession.Execute(model.Sessions[1]);
        model.ChooseLayout.Execute(model.Layouts[1]);
        Assert.Equal(["sway"], sessions);
        Assert.Equal(["de"], layouts);

        model.OptionsAvailable = false;
        model.OptionsOpen = false;
        model.ToggleOptions.Execute(null);
        Wait(() => view.OptionsMoving);
        Assert.False(view.OptionsButton.IsVisible);
        Assert.False(view.OptionsPanel.IsVisible);
    }

    [AvaloniaFact]
    public void Ease_of_access_and_power_open_from_their_corners()
    {
        var model = TwoUsers();
        model.BeginConversation(model.Users[0]);
        var contrast = new List<bool>();
        var scales = new List<double>();
        var power = new List<string>();
        model.HighContrastChanged += contrast.Add;
        model.TextScaleChanged += scales.Add;
        model.PowerRequested += power.Add;
        var view = new SignInView { DataContext = model };
        var window = Host(view);
        Assert.True(view.EaseButton.Bounds.Left < 100 && view.EaseButton.TranslatePoint(default, window)!.Value.Y > 600);
        Assert.True(view.PowerButton.TranslatePoint(default, window)!.Value.X > 1200);

        model.ToggleEase.Execute(null);
        Pump();
        Assert.True(view.EasePanel.IsVisible);
        view.HighContrastSwitch.IsChecked = true;
        Assert.Equal([true], contrast);
        model.ChooseTextScale.Execute("1.5");
        Assert.Equal([1.5], scales);

        model.TogglePower.Execute(null);
        Pump();
        Assert.True(view.PowerPanel.IsVisible);
        model.Power.Execute("restart");
        Assert.Equal(["restart"], power);
        Assert.False(model.PowerOpen);
    }

    [AvaloniaFact]
    public void High_contrast_turns_the_sign_in_background_black()
    {
        var model = TwoUsers();
        model.BeginConversation(model.Users[0]);
        var view = new SignInView { DataContext = model };
        Host(view);
        var app = Application.Current!;
        var normal = ((ISolidColorBrush)view.Root.Background!).Color;
        Assert.NotEqual(Colors.Black, normal);
        SignInTheme.SetHighContrast(app, true, null);
        Pump();
        Assert.Equal(Colors.Black, ((ISolidColorBrush)view.Root.Background!).Color);
        SignInTheme.SetHighContrast(app, false, null);
        Pump();
        Assert.Equal(normal, ((ISolidColorBrush)view.Root.Background!).Color);
    }

    [AvaloniaFact]
    public void The_network_flyout_lists_points_and_asks_for_a_key()
    {
        var model = new NetworkModel { Open = true, HasWifi = true };
        var chosen = new List<string>();
        model.Points.Add(new NetworkPointEntry("Home", "Secured", enabled: true, active: false, new RelayCommand(_ => model.AskingFor = "Home")));
        model.Points.Add(new NetworkPointEntry("Cafe", "Open", enabled: true, active: false, new RelayCommand(_ => chosen.Add("Cafe"))));
        var entered = new List<(string, string)>();
        model.PassphraseEntered += (ssid, key) => entered.Add((ssid, key));
        var view = new NetworkFlyoutView { DataContext = model };
        Host(view);
        Assert.True(view.Panel.IsVisible);
        Assert.Equal(2, view.PointList.ItemCount);
        model.Points[1].Choose.Execute(null);
        Assert.Equal(["Cafe"], chosen);
        model.Points[0].Choose.Execute(null);
        Pump();
        Assert.True(view.PassphrasePanel.IsVisible);
        model.Passphrase = "correct horse";
        model.Connect.Execute(null);
        Assert.Equal([("Home", "correct horse")], entered);
    }

    [AvaloniaFact]
    public void Welcome_covers_the_sign_in_view_on_the_accent_with_the_users_name()
    {
        var model = TwoUsers();
        model.ShowUsers(null);
        model.BeginConversation(model.Users[0]);
        model.OptionsOpen = true;
        var view = new SignInView { DataContext = model };
        var window = Host(view);
        var welcome = view.FindControl<Grid>("Welcome")!;
        Assert.False(welcome.IsVisible);

        model.ShowWelcome();
        Pump();
        Assert.True(welcome.IsEffectivelyVisible);
        Assert.False(model.OptionsOpen);
        Assert.True(model.Busy);
        Assert.Equal(model.CurrentName, view.FindControl<TextBlock>("WelcomeName")!.Text);
        Assert.Equal(5, view.FindControl<Canvas>("WelcomeDots")!.Children.Count);
        Assert.Equal(Accent, Assert.IsAssignableFrom<ISolidColorBrush>(welcome.Background).Color);
        window.Close();
    }
}
