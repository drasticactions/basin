using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Basin;
using Basin.Audio.PipeWire;
using Basin.Config;
using Basin.Diagnostics;
using System7.Avalonia.Controls;
using System7.Avalonia.Rendering;

namespace System7Wm;

internal sealed partial class System7Shell
{
    private int _patternIndex = -1;
    private AudioSession? _audio;
    private SinkVolume? _sinkVolume;
    private System7SoundSlider? _volumeSlider;
    private bool _audioTried;
    private bool _syncingSlider;

    private void OpenPanel(string name)
    {
        switch (name)
        {
            case "Monitors":
                OpenMonitors();
                break;
            case "Sound":
                OpenSound();
                break;
            case "Desktop Patterns":
                OpenPatterns();
                break;
            case "Mouse":
                OpenMouse();
                break;
            case "Keyboard":
                OpenKeyboard();
                break;
            case "WindowShade":
                OpenWindowShade();
                break;
        }
    }

    private static StackPanel Column(params Control[] children)
    {
        var panel = new StackPanel { Spacing = 6, Margin = new Thickness(12) };
        foreach (var child in children)
        {
            panel.Children.Add(child);
        }

        return panel;
    }

    private static StackPanel RadioRow<T>(string group, IEnumerable<(string Label, T Value)> choices, T current, Action<T> chose, Orientation orientation = Orientation.Horizontal)
    {
        var row = new StackPanel { Orientation = orientation, Spacing = orientation == Orientation.Horizontal ? 10 : 2 };
        foreach (var (label, value) in choices)
        {
            var radio = new RadioButton { Content = label, GroupName = group, IsChecked = EqualityComparer<T>.Default.Equals(value, current) };
            radio.IsCheckedChanged += (_, _) =>
            {
                if (radio.IsChecked == true)
                {
                    chose(value);
                }
            };
            row.Children.Add(radio);
        }

        return row;
    }

    private void OpenMonitors()
    {
        var arrangement = new Canvas { Width = 280, Height = 110, Background = new SolidColorBrush(Color.FromRgb(0xcc, 0xcc, 0xcc)) };
        void DrawArrangement()
        {
            arrangement.Children.Clear();
            var views = _outputs.Views;
            if (views.Count == 0)
            {
                return;
            }

            var bounds = _layout.Bounds;
            var scale = Math.Min(260.0 / Math.Max(1, bounds.Width), 90.0 / Math.Max(1, bounds.Height));
            foreach (var view in views)
            {
                var box = _layout.BoxOf(view.Output);
                var x = 10 + ((box.X - bounds.X) * scale);
                var y = 10 + ((box.Y - bounds.Y) * scale);
                var screen = new Border
                {
                    Width = box.Width * scale,
                    Height = box.Height * scale,
                    BorderBrush = Brushes.Black,
                    BorderThickness = new Thickness(1),
                    Background = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0xcc)),
                    Tag = view.Output.Name,
                };
                Canvas.SetLeft(screen, x);
                Canvas.SetTop(screen, y);
                arrangement.Children.Add(screen);
                var label = new TextBlock { Text = view.Output.Name, Foreground = Brushes.White, MaxWidth = Math.Max(0, (box.Width * scale) - 8), TextTrimming = TextTrimming.CharacterEllipsis };
                Canvas.SetLeft(label, x + 4);
                Canvas.SetTop(label, y + 10);
                arrangement.Children.Add(label);
                if (ReferenceEquals(view, MainView))
                {
                    var bar = new Rectangle { Width = box.Width * scale, Height = 4, Fill = Brushes.White, Stroke = Brushes.Black, StrokeThickness = 1, Cursor = new Cursor(StandardCursorType.Hand) };
                    Canvas.SetLeft(bar, x);
                    Canvas.SetTop(bar, y);
                    arrangement.Children.Add(bar);
                }
            }
        }

        var dragging = false;
        arrangement.PointerPressed += (_, e) =>
        {
            var point = e.GetPosition(arrangement);
            dragging = arrangement.Children.OfType<Rectangle>().Any(bar =>
                point.X >= Canvas.GetLeft(bar) && point.X < Canvas.GetLeft(bar) + bar.Width && point.Y >= Canvas.GetTop(bar) - 2 && point.Y < Canvas.GetTop(bar) + 8);
        };
        arrangement.PointerReleased += (_, e) =>
        {
            if (!dragging)
            {
                return;
            }

            dragging = false;
            var point = e.GetPosition(arrangement);
            foreach (var screen in arrangement.Children.OfType<Border>())
            {
                if (point.X >= Canvas.GetLeft(screen) && point.X < Canvas.GetLeft(screen) + screen.Width &&
                    point.Y >= Canvas.GetTop(screen) && point.Y < Canvas.GetTop(screen) + screen.Height && screen.Tag is string name)
                {
                    SetMainOutput(name);
                    DrawArrangement();
                    break;
                }
            }
        };
        DrawArrangement();
        var depths = RadioRow("depth", ColorDepths.All.Select(d => (d.Label, d.Depth)), _config.Depth, depth =>
        {
            _config.Depth = depth;
            SaveSetting("display", "depth", TomlValue.From(ColorDepths.NameOf(depth)));
            ApplyConfig();
            BasinReport.Line($"DEPTH {ColorDepths.NameOf(depth)}");
        }, Orientation.Vertical);
        var applications = new CheckBox
        {
            Name = "applyToApplications",
            Content = "Applications too",
            IsChecked = _config.DepthApplications,
            IsEnabled = ApplicationDepthAvailable,
        };
        applications.IsCheckedChanged += (_, _) =>
        {
            _config.DepthApplications = applications.IsChecked == true;
            SaveSetting("display", "apply_to_applications", TomlValue.From(_config.DepthApplications));
            ApplyApplicationDepth();
            BasinReport.Line($"APPDEPTH {(_config.DepthApplications ? "on" : "off")}");
        };
        depths.Children.Add(applications);
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(12),
            Spacing = 16,
            Children =
            {
                Column(new TextBlock { Text = "Characteristics of selected monitor:", TextWrapping = TextWrapping.Wrap, MaxWidth = 180 }, depths),
                Column(new TextBlock { Text = "Drag the menu bar to set the main monitor.", TextWrapping = TextWrapping.Wrap, MaxWidth = arrangement.Width }, arrangement),
            },
        };
        OpenShellWindow("panel:Monitors", "Monitors", content, 560, 230);
    }

    internal void SetMainOutput(string name)
    {
        var old = MainView;
        _config.SetMainOutput(name);
        SaveSetting("display", "main", TomlValue.From(name));
        BasinReport.Line($"MAIN {name}");
        RelayoutChrome();
        if (!ReferenceEquals(old, MainView))
        {
            _outputs.ScheduleAll();
        }
    }

    private void OpenSound()
    {
        var sink = SystemVolume();
        var level = _config.Volume >= 0 ? _config.Volume : sink is { IsAvailable: true } ? LevelOf(sink) : 4;
        var slider = new System7SoundSlider { Minimum = 0, Maximum = 7, Value = level, TickFrequency = 1, IsSnapToTickEnabled = true, Name = "volume" };
        var status = new TextBlock();
        var available = Muted || sink is { IsAvailable: true };
        slider.IsEnabled = available;
        status.Text = available ? "" : "There's no sound output, so you can't change the volume.";
        _volumeSlider = slider;
        slider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Avalonia.Controls.Primitives.RangeBase.ValueProperty && !_syncingSlider)
            {
                SetVolumeLevel((int)Math.Round(slider.Value));
            }
        };
        var beep = new Button { Content = "Beep", Width = 60, Height = 20, Name = "beep" };
        beep.Click += (_, _) => Beep();
        var sounds = System7AlertSound.All.Select(s => (Label: s.Name, Path: s == System7AlertSound.SimpleBeep ? "" : s.Name)).ToList();
        foreach (var folder in (string[])["/usr/share/sounds/freedesktop/stereo"])
        {
            if (Directory.Exists(folder))
            {
                sounds.AddRange(Directory.EnumerateFiles(folder).Order(StringComparer.Ordinal).Take(12)
                    .Select(file => (System.IO.Path.GetFileNameWithoutExtension(file).Replace('-', ' '), file)));
            }
        }

        var chooser = new ComboBox { Name = "alertSound", ItemsSource = sounds.Select(s => s.Label).ToList() };
        chooser.SelectedIndex = Math.Max(0, sounds.FindIndex(s => s.Path == _config.AlertSound));
        chooser.SelectionChanged += (_, _) =>
        {
            if (chooser.SelectedIndex < 0)
            {
                return;
            }

            var (label, path) = sounds[chooser.SelectedIndex];
            _config.SetAlertSound(path);
            SaveSetting("sound", "alert", TomlValue.From(path));
            BasinReport.Line($"ALERTSOUND {label}");
            Beep();
        };
        var content = Column(
            new TextBlock { Text = "Speaker Volume" },
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20, Children = { slider, Column(new TextBlock { Text = "Alert Sound" }, chooser, beep) } },
            status);
        OpenShellWindow("panel:Sound", "Sound", content, 300, 170);
    }

    internal void SetVolumeLevel(int value)
    {
        value = Math.Clamp(value, 0, 7);
        _config.Volume = value;
        SaveSetting("sound", "volume", TomlValue.From((long)value));
        if (!Muted)
        {
            _ = SystemVolume()?.SetVolume(value / 7f);
        }

        BasinReport.Line($"VOLUME {value}");
        if (_volumeSlider is { } slider && (int)Math.Round(slider.Value) != value)
        {
            _syncingSlider = true;
            slider.Value = value;
            _syncingSlider = false;
        }
    }

    private void ReportVolume()
    {
        var volume = SystemVolume();
        _ = _audio?.Roundtrip();
        if (volume is not { IsAvailable: true } sink)
        {
            BasinReport.Line($"SINK none level={(_config.Volume >= 0 ? _config.Volume : 4)}");
            return;
        }

        BasinReport.Line($"SINK {sink.SinkName} level={LevelOf(sink)} volume={sink.Volume:0.000} muted={(sink.Muted ? "true" : "false")} route={(sink.UsesRoute ? "true" : "false")}");
    }

    private SinkVolume? SystemVolume()
    {
        if (Muted)
        {
            return null;
        }

        if (!_audioTried)
        {
            _audioTried = true;
            _audio = AudioSession.TryCreate(_host.Loop, out var reason, clientName: "system7-wm");
            if (_audio is null)
            {
                _log.Warn($"sound: the volume cannot be set, {reason}");
                return null;
            }

            _sinkVolume = new SinkVolume(_audio);
            if (!_audio.WaitFor(() => _sinkVolume.IsAvailable, 1000))
            {
                _log.Warn($"sound: PipeWire at {_audio.Remote} has no default sink with a volume");
            }
        }

        return _sinkVolume;
    }

    private void DisposeVolume()
    {
        _sinkVolume?.Dispose();
        _sinkVolume = null;
        _audio?.Dispose();
        _audio = null;
    }

    private static int LevelOf(SinkVolume sink) => (int)Math.Clamp(Math.Round(sink.Volume * 7), 0, 7);

    private static bool OnPath(string program)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            if (File.Exists(System.IO.Path.Combine(directory, program)))
            {
                return true;
            }
        }

        return false;
    }

    private void OpenPatterns()
    {
        var patterns = System7DesktopPattern.All;
        if (_patternIndex < 0)
        {
            _patternIndex = Math.Max(0, patterns.ToList().FindIndex(p => p.Name == _config.Pattern));
        }

        var preview = new Image { Width = 96, Height = 96, Stretch = Stretch.None };
        var name = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center };
        void Show()
        {
            var pattern = patterns[_patternIndex];
            preview.Source = PatternBitmap(pattern, _config.Depth, 96);
            name.Text = pattern.Name;
        }

        static Avalonia.Controls.Shapes.Path Arrow(string data) => new()
        {
            Data = StreamGeometry.Parse(data),
            Fill = Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var previous = new Button
        {
            Content = Arrow("M3,0h1v8h-1z M2,1h1v6h-1z M1,2h1v4h-1z M0,3h1v2h-1z"),
            Width = 28,
            Height = 20,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        var next = new Button
        {
            Content = Arrow("M0,0h1v8h-1z M1,1h1v6h-1z M2,2h1v4h-1z M3,3h1v2h-1z"),
            Width = 28,
            Height = 20,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        previous.Click += (_, _) =>
        {
            _patternIndex = (_patternIndex + patterns.Count - 1) % patterns.Count;
            Show();
        };
        next.Click += (_, _) =>
        {
            _patternIndex = (_patternIndex + 1) % patterns.Count;
            Show();
        };
        var set = new Button { Content = "Set Desktop Pattern", Width = 150, Height = 20 };
        set.Click += (_, _) => SetPattern(patterns[_patternIndex].Name);
        Show();
        var content = Column(
            new Border { BorderBrush = Brushes.Black, BorderThickness = new Thickness(1), Child = preview, HorizontalAlignment = HorizontalAlignment.Center },
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, Children = { previous, name, next } },
            set);
        OpenShellWindow("panel:Desktop Patterns", "Desktop Patterns", content, 240, 190);
    }

    internal void SetPattern(string name)
    {
        _config.Pattern = name;
        SaveSetting("desktop", "pattern", TomlValue.From(name));
        ApplyConfig();
        BasinReport.Line($"PATTERN {name}");
    }

    private static unsafe WriteableBitmap PatternBitmap(System7DesktopPattern pattern, System7ColorDepth depth, int size)
    {
        var bitmap = new WriteableBitmap(new PixelSize(size, size), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var frame = bitmap.Lock();
        var color = depth != System7ColorDepth.Monochrome && pattern.HasColor;
        for (var y = 0; y < size; y++)
        {
            var row = (uint*)((byte*)frame.Address + (y * frame.RowBytes));
            for (var x = 0; x < size; x++)
            {
                row[x] = color ? DepthColors.Map(pattern.Argb(x, y), depth) : pattern.IsSet(x, y) ? 0xff000000u : 0xffffffffu;
            }
        }

        return bitmap;
    }

    private void OpenMouse()
    {
        var speeds = new (string, double)[] { ("Very Slow", 0.0), ("Slow", 0.25), ("", 0.4), ("", 0.55), ("", 0.7), ("Fast", 0.85), ("Tablet", 1.0) };
        var current = _config.Tracking ?? 0.55;
        var nearest = speeds.MinBy(s => Math.Abs(s.Item2 - current)).Item2;
        var tracking = RadioRow("tracking", speeds, nearest, value =>
        {
            _config.Tracking = value;
            SaveSetting("mouse", "tracking", TomlValue.From(value));
            ApplyInput();
            BasinReport.Line($"TRACKING {value:0.##}");
        });
        var clicks = RadioRow("double", new (string, int)[] { ("Slow", 800), ("Medium", 500), ("Fast", 300) },
            _config.DoubleClickMs >= 650 ? 800 : _config.DoubleClickMs >= 400 ? 500 : 300, value =>
            {
                _config.DoubleClickMs = value;
                SaveSetting("finder", "double_click_ms", TomlValue.From((long)value));
                BasinReport.Line($"DOUBLECLICK {value}");
            });
        OpenShellWindow("panel:Mouse", "Mouse", Column(new TextBlock { Text = "Mouse Tracking" }, tracking, new TextBlock { Text = "Double-Click Speed" }, clicks), 420, 120);
    }

    private void OpenKeyboard()
    {
        var rates = RadioRow("rate", new (string, int)[] { ("Slow", 8), ("", 15), ("", 25), ("Fast", 40) },
            new[] { 8, 15, 25, 40 }.MinBy(r => Math.Abs(r - _config.RepeatRate)), value =>
            {
                _config.RepeatRate = value;
                SaveSetting("keyboard", "repeat_rate", TomlValue.From((long)value));
                ApplyInput();
                BasinReport.Line($"REPEAT rate={value}");
            });
        var delays = RadioRow("delay", new (string, int)[] { ("Off", 0), ("Long", 900), ("", 600), ("Short", 300) },
            _config.RepeatRate == 0 ? 0 : new[] { 900, 600, 300 }.MinBy(d => Math.Abs(d - _config.RepeatDelay)), value =>
            {
                if (value == 0)
                {
                    _config.RepeatRate = 0;
                    SaveSetting("keyboard", "repeat_rate", TomlValue.From(0L));
                }
                else
                {
                    _config.RepeatDelay = value;
                    SaveSetting("keyboard", "repeat_delay", TomlValue.From((long)value));
                }

                ApplyInput();
                BasinReport.Line($"REPEAT delay={value}");
            });
        OpenShellWindow("panel:Keyboard", "Keyboard", Column(new TextBlock { Text = "Key Repeat Rate" }, rates, new TextBlock { Text = "Delay Until Repeat" }, delays), 360, 120);
    }

    private void OpenWindowShade()
    {
        var onOff = RadioRow("shade", new (string, bool)[] { ("On", true), ("Off", false) }, _config.ShadeEnabled, value =>
        {
            _config.ShadeEnabled = value;
            SaveSetting("windowshade", "enabled", TomlValue.From(value));
        });
        var clicks = RadioRow("clicks", new (string, int)[] { ("1", 1), ("2", 2), ("3", 3) }, _config.ShadeClicks, value =>
        {
            _config.ShadeClicks = value;
            SaveSetting("windowshade", "clicks", TomlValue.From((long)value));
        });
        var modifier = RadioRow("modifier", new (string, string)[] { ("None", "none"), ("⌘", "command"), ("Option", "option"), ("Control", "control"), ("Shift", "shift") },
            _config.ShadeModifier, value =>
            {
                _config.ShadeModifier = value;
                SaveSetting("windowshade", "modifier", TomlValue.From(value));
            });
        var sound = new CheckBox { Content = "Make sounds", IsChecked = _config.ShadeSound };
        sound.IsCheckedChanged += (_, _) =>
        {
            _config.ShadeSound = sound.IsChecked == true;
            SaveSetting("windowshade", "sound", TomlValue.From(_config.ShadeSound));
        };
        OpenShellWindow("panel:WindowShade", "WindowShade", Column(
            new TextBlock { Text = "WindowShade" }, onOff,
            new TextBlock { Text = "Number of clicks" }, clicks,
            new TextBlock { Text = "Modifier key" }, modifier,
            sound), 360, 190);
    }
}
