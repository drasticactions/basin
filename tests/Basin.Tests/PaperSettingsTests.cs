using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.UI.Paper;
using Xunit;

namespace Basin.Tests;

public sealed class PaperSettingsTests
{
    private const uint BtnLeft = 0x110;
    private const int Width = 640;
    private const int Height = 440;

    public static TheoryData<string, string, string> GoldenRows => new()
    {
        { "gl", "gl", "paper-form-gl" },
        { "skia-gl", "gl", "paper-form-gl" },
        { "impeller", "gl", "paper-form-gl" },
        { "vulkan", "vulkan", "paper-form-vk" },
        { "skia-vulkan", "vulkan", "paper-form-vk" },
        { "skia-graphite", "vulkan", "paper-form-vk" },
    };

    public static TheoryData<string, string> Rows => new()
    {
        { "gl", "gl" },
        { "vulkan", "vulkan" },
    };

    [Theory]
    [MemberData(nameof(GoldenRows))]
    public void Golden_paper_settings_form(string row, string backend, string golden)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        Assert.SkipWhen(!CompositorTestHost.GoldensComparable(row), $"{row} goldens are not comparable on this driver");
        using var rig = Rig.Open(row, backend);
        var form = new DemoForm();
        rig.Surface.Build = form.Build;
        rig.Settle();

        Assert.True(rig.Surface.TryAcquire(out var frame));
        var buffer = frame.Buffer!;
        Assert.True(BufferCapture.TryReadRgba(buffer, rig.Host.Renderer, out var rgba));
        var dump = Environment.GetEnvironmentVariable("BASIN_PAPER_DUMP");
        if (!string.IsNullOrEmpty(dump))
        {
            File.WriteAllBytes(dump, PngCodec.Encode(rgba!, buffer.Width, buffer.Height));
        }

        Golden.AssertMatches(rgba!, buffer.Width, buffer.Height, golden, tolerance: 6);
        frame.Dispose();
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void Each_widget_edits_its_setting(string row, string backend)
    {
        using var rig = Rig.Open(row, backend);
        var form = new DemoForm();
        rig.Surface.Build = form.Build;
        rig.Surface.KeyText = new DigitKeys();
        rig.Settle();

        rig.Click(80, 54);
        Assert.Equal(1, form.Section);

        rig.Click(597, 64);
        Assert.True(form.WobblyValue);
        rig.Click(597, 64);
        Assert.False(form.WobblyValue);

        rig.Click(500, 98);
        rig.Click(300, 204);
        Assert.Equal("quill", form.StyleValue);

        rig.Click(372 + 87, 132);
        Assert.Equal(0.5, form.ScaleValue, 3);

        rig.Click(500, 166);
        Assert.True(rig.Surface.WantsTextInput);
        rig.Chord(KeyLeftCtrl, KeyA);
        rig.Type(Key1);
        rig.Type(Key8);
        Assert.Equal(14, form.FontSizeValue);
        rig.Type(KeyEnter);
        Assert.Equal(18, form.FontSizeValue);

        rig.Click(500, 268);
        Assert.Same(form.Chord, form.Capture.Target);
        Assert.True(form.Capture.Offer("Super+x"));
        Assert.Equal("Super+x", form.ChordValue);

        rig.Click(601, 302);
        Assert.Equal(["bloom", "crt", "grain"], form.Stages);
        rig.Click(577, 339);
        Assert.Equal(["crt", "grain"], form.Stages);
        Assert.Equal(2, form.Changes);
    }

    [Theory]
    [InlineData("#4c8df6")]
    [InlineData("#2a35c0")]
    [InlineData("#000000")]
    [InlineData("#ffffff")]
    [InlineData("#e0a33a")]
    [InlineData("#808080")]
    public void A_color_survives_the_round_trip_through_hsv(string hex)
    {
        var (hue, saturation, value) = SettingsWidgets.ToHsv(Parse(hex));
        Assert.Equal(hex, SettingsWidgets.Hex(SettingsWidgets.FromHsv(hue, saturation, value)));
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void The_color_picker_opens_from_the_swatch_and_writes_hex(string row, string backend)
    {
        using var rig = Rig.Open(row, backend, Width, 640);
        var form = new DemoForm();
        rig.Surface.Build = form.Build;
        rig.Settle();

        rig.Click(381, 234);
        if (Environment.GetEnvironmentVariable("BASIN_PAPER_DUMP") is { Length: > 0 } dump)
        {
            Assert.True(rig.Surface.TryAcquire(out var frame));
            Assert.True(BufferCapture.TryReadRgba(frame.Buffer!, rig.Host.Renderer, out var rgba));
            File.WriteAllBytes(dump, PngCodec.Encode(rgba!, frame.Buffer!.Width, frame.Buffer.Height));
            frame.Dispose();
        }

        var square = PickerSquare(rig);
        rig.Click(square.X + square.Size - 1, square.Y + 1);
        var (hue, saturation, value) = SettingsWidgets.ToHsv(Parse(form.TintValue));
        Assert.True(saturation > 0.95f && value > 0.95f, $"the top-right corner picked {form.TintValue}");
        var before = hue;

        rig.Click(360, square.Y + (square.Size / 2));
        (hue, _, _) = SettingsWidgets.ToHsv(Parse(form.TintValue));
        Assert.InRange(hue, 0.45f, 0.55f);
        Assert.NotEqual(before, hue);

        rig.Click(391, 259);
        Assert.Equal("#1b1d23", form.TintValue);
    }

    private static (int X, int Y, int Size) PickerSquare(Rig rig) => (184, 250, (int)MathF.Round(14f * 11f));

    private static Prowl.Vector.Color32 Parse(string hex)
    {
        Assert.True(SettingsWidgets.TryParseColor(hex, out var color), hex);
        return color;
    }

    private const uint Key1 = 2;
    private const uint Key8 = 9;
    private const uint KeyA = 30;
    private const uint KeyLeftCtrl = 29;
    private const uint KeyEnter = 28;

    private sealed class DigitKeys : IKeyText
    {
        public int TextFor(uint key, Span<char> into)
        {
            if (key is < 2 or > 11)
            {
                return 0;
            }

            into[0] = key == 11 ? '0' : (char)('0' + (key - 1));
            return 1;
        }
    }

    internal sealed class DemoForm
    {
        private readonly Prowl.Scribe.FontFile _face = Basin.Frames.Quill.QuillFrameFonts.Bundled();
        private readonly ChordCapture _capture = new();

        public DemoForm()
        {
            Theme = SettingsTheme.Dark(_face, 14f);
            Wobbly = new SettingValue<bool>(() => WobblyValue, v => WobblyValue = v, () => WobblyValue);
            Style = new SettingValue<string>(() => StyleValue, v => StyleValue = v, badge: () => "applies after restart");
            Scale = new SettingValue<double>(() => ScaleValue, v => ScaleValue = v, () => ScaleValue != 0.25);
            FontSize = new SettingValue<double>(() => FontSizeValue, v => FontSizeValue = v);
            Theme_ = new SettingValue<string>(() => ThemeValue, v => ThemeValue = v, error: () => ThemeValue.Length == 0 ? "required" : null);
            Tint = new SettingValue<string>(() => TintValue, v => TintValue = v);
            Chord = new SettingValue<string>(() => ChordValue, v => ChordValue = v);
        }

        public SettingsTheme Theme { get; }

        public ChordCapture Capture => _capture;

        public bool WobblyValue { get; set; }

        public string StyleValue { get; set; } = "flat";

        public double ScaleValue { get; set; } = 0.25;

        public double FontSizeValue { get; set; } = 14;

        public string ThemeValue { get; set; } = "Menta";

        public string TintValue { get; set; } = "#4c8df6";

        public string ChordValue { get; set; } = "Super+comma";

        public List<string> Stages { get; } = ["bloom", "crt"];

        public int Section { get; set; }

        public int Changes { get; private set; }

        public ISettingValue<bool> Wobbly { get; }

        public ISettingValue<string> Style { get; }

        public ISettingValue<double> Scale { get; }

        public ISettingValue<double> FontSize { get; }

        public ISettingValue<string> Theme_ { get; }

        public ISettingValue<string> Tint { get; }

        public ISettingValue<string> Chord { get; }

        public void Build(Prowl.PaperUI.Paper paper)
        {
            using (paper.Row("root").Size(paper.Stretch()).BackgroundColor(Theme.Background).Enter())
            {
                SettingsWidgets.SectionNav(paper, Theme, "nav", ["Frames", "Effects", "Bindings"], Section, i => Section = i, [false, true, false]);
                using (SettingsWidgets.Scroll(paper, Theme, "body"))
                {
                    SettingsWidgets.Heading(paper, Theme, "heading", "Frames");
                    SettingsWidgets.ToggleRow(paper, Theme, "wobbly", "Wobbly windows", Wobbly);
                    SettingsWidgets.EnumRow(paper, Theme, "style", "Frame style", Style, ["beos", "flat", "metacity", "quill", "none"]);
                    SettingsWidgets.SliderRow(paper, Theme, "scale", "Edge scale", Scale, 0, 1, 0.05);
                    SettingsWidgets.NumberRow(paper, Theme, "font", "Font size", FontSize, "0", 6, 72);
                    SettingsWidgets.TextRow(paper, Theme, "theme", "Metacity theme", Theme_);
                    SettingsWidgets.ColorRow(paper, Theme, "tint", "Tint", Tint);
                    SettingsWidgets.ChordRow(paper, Theme, "chord", "Open settings", Chord, _capture);
                    SettingsWidgets.ListRow(
                        paper, Theme, "post", "Post stages", Stages,
                        (p, i) => SettingsWidgets.Note(p, Theme, "stage", Stages[i], Theme.Text),
                        () => "grain", () => Changes++);
                    SettingsWidgets.Note(paper, Theme, "note", "Rules apply to windows mapped after the change.");
                }
            }
        }
    }

    internal sealed class Rig : IDisposable
    {
        private readonly QuillHostTests.QuillLease _lease;
        private readonly PaperSurfaceTests.FakeClock _clock = new();

        private Rig(CompositorTestHost host, QuillHostTests.QuillLease lease, int width, int height)
        {
            Host = host;
            _lease = lease;
            Paper = new PaperUIHost(lease.Host, clockNanos: _clock.Now);
            Surface = PaperSurfaceTests.Create(Paper, width, height);
        }

        public CompositorTestHost Host { get; }

        public PaperUIHost Paper { get; }

        public PaperSurface Surface { get; }

        public static Rig Open(string row, string backend, int width = Width, int height = Height)
        {
            CompositorTestHost.SkipUnlessRunnable(row);
            CompositorTestHost.SkipUnlessRunnable(backend);
            var host = new CompositorTestHost(renderer: row);
            var lease = QuillHostTests.QuillLease.Open(host, backend);
            if (lease is null)
            {
                host.Dispose();
                Assert.Skip($"this row builds no {backend} quill host");
            }

            return new Rig(host, lease!, width, height);
        }

        public int Settle() => PaperSurfaceTests.Settle(Paper, _clock);

        public void Idle(int millis)
        {
            for (var elapsed = 0; elapsed < millis; elapsed += 16)
            {
                _clock.Advance(16);
                Assert.Null(Paper.NextDueMillis);
                Paper.Pump();
            }
        }

        public void Click(double x, double y)
        {
            Surface.NotifyPointerMotion(1, x, y);
            Surface.NotifyPointerButton(2, BtnLeft, true);
            Surface.NotifyPointerButton(3, BtnLeft, false);
            Settle();
        }

        public void Type(uint key)
        {
            Surface.NotifyKey(1, key, true);
            Surface.NotifyKey(2, key, false);
            Settle();
        }

        public void Chord(uint modifier, uint key)
        {
            Surface.NotifyKey(1, modifier, true);
            Surface.NotifyKey(2, key, true);
            Surface.NotifyKey(3, key, false);
            Surface.NotifyKey(4, modifier, false);
            Settle();
        }

        public void Dispose()
        {
            Surface.Dispose();
            Paper.Dispose();
            _lease.Dispose();
            Host.Dispose();
        }
    }
}
