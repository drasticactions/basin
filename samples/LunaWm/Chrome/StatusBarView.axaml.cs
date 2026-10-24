using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace LunaWm;

public sealed partial class StatusBarView : UserControl
{
    private readonly Avalonia.Threading.DispatcherTimer _scroll = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private double _offset;
    private double _overflow;
    private int _pause;

    public const double PullDistance = 18;

    private Avalonia.Point? _pullStart;
    private bool _pulled;

    public StatusBarView()
    {
        InitializeComponent();
        AddHandler(PointerPressedEvent, OnPullPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnPullMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnPullReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel, handledEventsToo: true);
        _scroll.Tick += (_, _) => ScrollBanner();
        DataContextChanged += (_, _) =>
        {
            if (Model is { } model)
            {
                model.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(StatusBarModel.Banner))
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(StartScroll, Avalonia.Threading.DispatcherPriority.Background);
                    }
                };
            }
        };
    }

    public bool Scrolling => _scroll.IsEnabled;

    private void StartScroll()
    {
        var text = this.FindControl<TextBlock>("BannerText")!;
        var track = this.FindControl<Canvas>("BannerTrack")!;
        text.Measure(Avalonia.Size.Infinity);
        var width = text.DesiredSize.Width;
        track.Width = Math.Min(width, 380);
        _overflow = Math.Max(0, width - track.Width);
        _offset = 0;
        _pause = 20;
        Canvas.SetLeft(text, 0);
        if (_overflow > 0)
        {
            _scroll.Start();
        }
        else
        {
            _scroll.Stop();
        }
    }

    private void ScrollBanner()
    {
        if (_pause > 0)
        {
            _pause--;
            return;
        }

        _offset = Math.Min(_overflow, _offset + 2);
        Canvas.SetLeft(this.FindControl<TextBlock>("BannerText")!, -_offset);
        if (_offset >= _overflow)
        {
            _scroll.Stop();
        }
    }

    private StatusBarModel? Model => DataContext as StatusBarModel;

    private void OnPullPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetPosition(this);
        _pulled = false;
        _pullStart = point.X >= Bounds.Width / 2 ? point : null;
    }

    private void OnPullMoved(object? sender, PointerEventArgs e)
    {
        if (_pullStart is not { } start || _pulled)
        {
            return;
        }

        if (e.GetPosition(this).Y - start.Y > PullDistance)
        {
            _pulled = true;
            Model?.Press("pull");
        }
    }

    private void OnPullReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_pulled)
        {
            e.Handled = true;
        }

        _pullStart = null;
        _pulled = false;
    }

    private void OnTitle(object? sender, PointerReleasedEventArgs e) => Model?.Press("title");

    private void OnNotifications(object? sender, PointerReleasedEventArgs e) => Model?.Press("notifications");

    private void OnBluetooth(object? sender, PointerReleasedEventArgs e) => Model?.Press("bluetooth");

    private void OnWifi(object? sender, PointerReleasedEventArgs e) => Model?.Press("wifi");

    private void OnSound(object? sender, PointerReleasedEventArgs e) => Model?.Press("sound");

    private void OnBattery(object? sender, PointerReleasedEventArgs e) => Model?.Press("battery");

    private void OnTitleInvoked(object? sender, RoutedEventArgs e) => Model?.Press("title");

    private void OnNotificationsInvoked(object? sender, RoutedEventArgs e) => Model?.Press("notifications");

    private void OnBluetoothInvoked(object? sender, RoutedEventArgs e) => Model?.Press("bluetooth");

    private void OnWifiInvoked(object? sender, RoutedEventArgs e) => Model?.Press("wifi");

    private void OnSoundInvoked(object? sender, RoutedEventArgs e) => Model?.Press("sound");

    private void OnBatteryInvoked(object? sender, RoutedEventArgs e) => Model?.Press("battery");
}
