using System.Runtime.CompilerServices;
using Basin.Capabilities;
using Basin.Config;
using Basin.Diagnostics;
using Basin.Scene;
using Basin.UI.Paper;
using TinyComp;
using Xunit;

namespace Basin.Tests;

public sealed class SettingsPanelTests
{
    private const int Width = 880;
    private const int Height = 620;
    private const int Rounds = 20;

    public static TheoryData<string, string, string> GoldenRows => new()
    {
        { "gl", "gl", "paper-panel-effects-gl" },
        { "skia-gl", "gl", "paper-panel-effects-gl" },
        { "impeller", "gl", "paper-panel-effects-gl" },
        { "vulkan", "vulkan", "paper-panel-effects-vk" },
        { "skia-vulkan", "vulkan", "paper-panel-effects-vk" },
        { "skia-graphite", "vulkan", "paper-panel-effects-vk" },
    };

    public static TheoryData<string, string> Rows => new()
    {
        { "gl", "gl" },
        { "vulkan", "vulkan" },
    };

    [Fact]
    public void Setting_a_default_the_file_lacks_writes_nothing()
    {
        using var file = new SeededFile();
        var draft = SettingsDraft.Open(file.Path);
        var wobbly = SettingsCatalog.Find("effects.wobbly")!;
        var offload = SettingsCatalog.Find("compositor.offload")!;

        draft.Set(wobbly, TomlValue.From(true));
        draft.Set(offload, TomlValue.From(true));
        Assert.Equal(1, draft.DirtyCount);
        Assert.False(draft.Document.Contains("compositor", "offload"));

        draft.Set(wobbly, TomlValue.From(false));
        Assert.Equal(0, draft.DirtyCount);
        Assert.Equal(draft.FileText, draft.Text);
    }

    [Fact]
    public void A_draft_counts_its_dirty_keys_and_saves_only_them()
    {
        using var file = new SeededFile();
        var draft = SettingsDraft.Open(file.Path);
        draft.Set(SettingsCatalog.Find("canvas.edge_scale")!, TomlValue.From(0.15));
        draft.Set("bindings", "Super+x", TomlValue.From("bell"));
        draft.Edit(d => d.SetInTable("rule", d.AppendTable("rule"), "app_id", TomlValue.From("mpv")), "rule");

        Assert.Equal(3, draft.DirtyCount);
        Assert.True(draft.IsDirty("canvas.edge_scale"));
        Assert.True(draft.IsDirty("rule"));
        Assert.False(draft.IsDirty("effects"));

        Assert.Null(draft.Save(overwrite: false));
        Assert.Equal(0, draft.DirtyCount);
        var saved = File.ReadAllText(file.Path);
        Assert.Contains("\"Super+x\" = \"bell\"", saved, StringComparison.Ordinal);
        Assert.Contains("[[rule]]\napp_id = \"mpv\"", saved, StringComparison.Ordinal);
        Assert.Contains("edge_scale   = 0.15           # draw scale", saved, StringComparison.Ordinal);
    }

    [Fact]
    public void Resetting_effects_puts_each_line_back_to_its_default_and_keeps_the_comments()
    {
        using var file = new SeededFile();
        var form = Form(file.Path);
        var draft = form.Draft;
        draft.Set(SettingsCatalog.Find("effects.wobbly")!, TomlValue.From(true));
        draft.Set(SettingsCatalog.Find("effects.open")!, TomlValue.From("fade"));
        draft.Edit(
            d =>
            {
                d.Remove("effects", "shader");
                d.SetInTable("effects.shader", d.AppendTable("effects.shader"), "path", TomlValue.From("x.slangp"));
            },
            "effects.shader");
        Assert.Equal(3, draft.DirtyCount);

        form.ResetSection(SettingsCatalog.Effects);

        Assert.Equal(0, draft.DirtyCount);
        Assert.Equal(0, draft.Document.TableCount("effects.shader"));
        Assert.Contains("wobbly   = false             # wobble windows on interactive move", draft.Text, StringComparison.Ordinal);
        Assert.Contains("open     = \"none\"            # none | fade | zoom | glide | sheet", draft.Text, StringComparison.Ordinal);
        Assert.Contains("shader = \"none\"", draft.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Resetting_a_default_the_file_changed_writes_the_default_on_the_same_line()
    {
        using var file = new SeededFile();
        File.WriteAllText(file.Path, File.ReadAllText(file.Path).Replace("wobbly   = false", "wobbly   = true", StringComparison.Ordinal));
        var form = Form(file.Path);

        form.ResetSection(SettingsCatalog.Effects);

        Assert.Equal(1, form.Draft.DirtyCount);
        Assert.Contains("wobbly   = false             # wobble windows on interactive move", form.Draft.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Resetting_bindings_and_rules_clears_their_tables()
    {
        using var file = new SeededFile();
        var form = Form(file.Path);
        var draft = form.Draft;
        draft.Edit(d => d.SetInTable("rule", d.AppendTable("rule"), "app_id", TomlValue.From("mpv")), "rule");
        Assert.NotEmpty(draft.Document.Keys("bindings"));

        form.ResetSection(SettingsCatalog.Bindings);
        form.ResetSection(SettingsCatalog.Rules);

        Assert.Empty(draft.Document.Keys("bindings"));
        Assert.Equal(0, draft.Document.TableCount("rule"));
        Assert.Contains("[bindings]", draft.Text, StringComparison.Ordinal);
        var parsed = TinyComp.Config.Parse(draft.Text, BasinLog.For("t"), out var fatal);
        Assert.Null(fatal);
        Assert.Equal(9, parsed.Bindings.Count);
    }

    [Fact]
    public void Saving_over_a_file_changed_on_disk_asks_first()
    {
        using var file = new SeededFile();
        var draft = SettingsDraft.Open(file.Path);
        draft.Set(SettingsCatalog.Find("effects.wobbly")!, TomlValue.From(true));
        File.AppendAllText(file.Path, "\n# edited elsewhere\n");
        File.SetLastWriteTimeUtc(file.Path, DateTime.UtcNow.AddSeconds(5));

        Assert.NotNull(draft.Save(overwrite: false));
        Assert.True(draft.Conflict);
        Assert.DoesNotContain("wobbly   = true", File.ReadAllText(file.Path), StringComparison.Ordinal);

        Assert.Null(draft.Save(overwrite: true));
        Assert.False(draft.Conflict);
        Assert.Contains("wobbly   = true", File.ReadAllText(file.Path), StringComparison.Ordinal);
    }

    [Fact]
    public void Without_a_file_save_is_blocked_and_says_why()
    {
        var draft = SettingsDraft.Open(null);
        draft.Set(SettingsCatalog.Find("effects.wobbly")!, TomlValue.From(true));
        Assert.Equal(1, draft.DirtyCount);
        Assert.Contains("--config false", draft.Save(overwrite: false), StringComparison.Ordinal);
    }

    [Fact]
    public void Every_catalog_key_is_one_the_config_reads_without_a_warning()
    {
        var text = new System.Text.StringBuilder();
        foreach (var group in SettingsCatalog.Keys.GroupBy(key => key.Table))
        {
            text.Append('[').Append(group.Key).Append("]\n");
            foreach (var key in group)
            {
                var value = key.Default ?? (key.Kind == SettingKind.Choice ? "\"Menta\"" : "0.5");
                text.Append(TomlValue.Key(key.Key)).Append(" = ").Append(value).Append('\n');
            }
        }

        var sink = new CollectingSink();
        var previous = BasinLog.Sink;
        BasinLog.Sink = sink;
        try
        {
            _ = TinyComp.Config.Parse(text.ToString(), BasinLog.For("settings-test"), out _);
        }
        finally
        {
            BasinLog.Sink = previous;
        }

        Assert.DoesNotContain(sink.Lines, line => line.Contains("unknown", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [MemberData(nameof(GoldenRows))]
    public void Golden_settings_panel_effects_section(string row, string backend, string golden)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        Assert.SkipWhen(!CompositorTestHost.GoldensComparable(row), $"{row} goldens are not comparable on this driver");
        using var file = new SeededFile();
        using var rig = PaperSettingsTests.Rig.Open(row, backend, Width, Height);
        var form = Form(file.Path);
        form.Section = 3;
        form.Draft.Set(SettingsCatalog.Find("effects.wobbly")!, TomlValue.From(true));
        rig.Surface.Build = form.Build;
        rig.Settle();

        Assert.True(rig.Surface.TryAcquire(out var frame));
        var buffer = frame.Buffer!;
        Assert.True(BufferCapture.TryReadRgba(buffer, rig.Host.Renderer, out var rgba));
        if (Environment.GetEnvironmentVariable("BASIN_PAPER_DUMP") is { Length: > 0 } dump)
        {
            File.WriteAllBytes(dump, PngCodec.Encode(rgba!, buffer.Width, buffer.Height));
        }

        Golden.AssertMatches(rgba!, buffer.Width, buffer.Height, golden, tolerance: 6);
        frame.Dispose();
    }

    [Fact]
    public void The_panel_redrawn_after_one_motion_stays_within_budget() => AssertBudget("gl", "gl", "paper-panel-frame");

    [Fact]
    public void The_panel_redrawn_after_one_motion_on_vulkan_stays_within_budget() =>
        AssertBudget("vulkan", "vulkan", "paper-panel-frame-vk");

    [Theory]
    [MemberData(nameof(Rows))]
    public void An_idle_panel_draws_no_frame_in_a_second(string row, string backend)
    {
        using var file = new SeededFile();
        using var rig = PaperSettingsTests.Rig.Open(row, backend, Width, Height);
        rig.Surface.Build = Form(file.Path).Build;
        rig.Settle();
        rig.Surface.NotifyPointerMotion(1, 400, 300);
        rig.Settle();

        var settled = rig.Surface.Frames;
        rig.Idle(1000);
        Assert.Equal(settled, rig.Surface.Frames);
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public void Opening_and_closing_the_panel_fifty_times_leaves_nothing_behind(string row, string backend)
    {
        CompositorTestHost.SkipUnlessRunnable(row);
        CompositorTestHost.SkipUnlessRunnable(backend);
        using var file = new SeededFile();
        using var host = new CompositorTestHost(renderer: row);
        using var lease = QuillHostTests.QuillLease.Open(host, backend);
        Assert.SkipWhen(lease is null, $"this row builds no {backend} quill host");
        var clock = new PaperSurfaceTests.FakeClock();
        using var paper = new PaperUIHost(lease!.Host, clockNanos: clock.Now);
        var scene = new Scene.Scene();
        var index = new UISurfaceIndex();
        var draft = SettingsDraft.Open(file.Path);
        var textures = lease.TextureCount;
        var live = BasinCounters.LiveObjects;

        for (var i = 0; i < 50; i++)
        {
            var node = new UISurfaceNode(scene.Root, paper, index) { Target = UITargetKind.Dmabuf, InputEnabled = true };
            Assert.True(node.Configure(Width, Height, 1.0));
            var surface = (PaperSurface)node.Surface!;
            surface.Build = new SettingsForm(draft, Context()).Build;
            PaperSurfaceTests.Settle(paper, clock);
            Assert.True(surface.Frames > 0);
            node.Dispose();
        }

        Assert.Empty(paper.Surfaces);
        Assert.Equal(0, index.Count);
        Assert.Equal(textures, lease.TextureCount);
        Assert.Equal(live, BasinCounters.LiveObjects);
        scene.Root.Destroy();
    }

    private static void AssertBudget(string row, string backend, string path)
    {
        Budgets.Require();
        using var file = new SeededFile();
        using var rig = PaperSettingsTests.Rig.Open(row, backend, Width, Height);
        rig.Surface.Build = Form(file.Path).Build;
        rig.Settle();

        Move(rig, Rounds);
        var before = GC.GetAllocatedBytesForCurrentThread();
        Move(rig, Rounds);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Budgets.Check("server", path, allocated);
    }

    private static void Move(PaperSettingsTests.Rig rig, int rounds)
    {
        for (var i = 0; i < rounds; i++)
        {
            rig.Surface.NotifyPointerMotion((uint)i, 500 + (i & 1), 300);
            rig.Surface.Draw();
        }
    }

    private static SettingsForm Form(string path) => new(SettingsDraft.Open(path), Context());

    private static SettingsContext Context() => new()
    {
        Renderers = ["pixman", "gl", "vulkan"],
        MetacityThemes = ["Atlanta", "Menta"],
        FromFlags = new HashSet<string>(StringComparer.Ordinal) { "renderer" },
        Outputs = [new SettingsOutput("HEADLESS-1", ["1280x720"])],
        Capture = new ChordCapture(),
        Font = Basin.Frames.Quill.QuillFrameFonts.Bundled(),
    };

    private sealed class CollectingSink : IBasinLogSink
    {
        public List<string> Lines { get; } = [];

        public void Write(BasinLogLevel level, string category, ReadOnlySpan<char> message)
        {
            if (level >= BasinLogLevel.Warn)
            {
                Lines.Add(message.ToString());
            }
        }
    }

    private sealed class SeededFile : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("basin-settings-");

        public SeededFile([CallerFilePath] string sourcePath = "")
        {
            Path = System.IO.Path.Combine(_directory.FullName, "tinycomp.toml");
            File.Copy(
                System.IO.Path.Combine(System.IO.Path.GetDirectoryName(sourcePath)!, "..", "..", "samples", "TinyComp", "tinycomp.toml"),
                Path);
        }

        public string Path { get; }

        public void Dispose() => _directory.Delete(recursive: true);
    }
}
