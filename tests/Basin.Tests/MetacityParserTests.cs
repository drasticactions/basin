using Basin.Frames.Metacity;
using Xunit;

namespace Basin.Tests;

public sealed class MetacityParserTests
{
    internal static string FixtureRoot => Path.Combine(AppContext.BaseDirectory, "Fixtures", "metacity");

    internal static string Fixture(string theme, int major) =>
        Path.Combine(FixtureRoot, theme, MetacityThemes.SubDirectory, MetacityThemes.FileName(major));

    public static TheoryData<string, int> Fixtures => new() { { "Atlanta", 1 }, { "eOS", 1 }, { "eOS", 3 } };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Every_fixture_theme_loads(string theme, int major)
    {
        var loaded = MetacityTheme.ParseFile(Fixture(theme, major));
        Assert.Equal(theme, loaded.Name);
        Assert.NotEmpty(loaded.Info.Name);
        Assert.NotEmpty(loaded.Info.Author);
        Assert.True(loaded.FormatVersion >= major * 1000);
        Assert.NotNull(loaded.GetStyle(MetacityFrameType.Normal, MetacityFrameStateKind.Normal, MetacityResize.Both, MetacityFocus.Yes));
    }

    public static TheoryData<string> Corpus
    {
        get
        {
            var data = new TheoryData<string>();
            var names = MetacityThemes.Available();
            if (names.Count == 0)
            {
                data.Add(string.Empty);
            }

            foreach (var name in names)
            {
                data.Add(name);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void Every_installed_theme_loads(string name)
    {
        Assert.SkipWhen(name.Length == 0, "no metacity theme is installed under the XDG data directories");
        var theme = MetacityTheme.Load(name);
        Assert.NotEmpty(theme.Info.Name);
        Assert.Equal(MetacityThemes.Find(name), theme.FilePath);
    }

    [Fact]
    public void The_ladder_picks_the_newest_format_first()
    {
        var eos = Path.Combine(FixtureRoot, "eOS", MetacityThemes.SubDirectory);
        var theme = MetacityTheme.ParseFile(Path.Combine(eos, MetacityThemes.FileName(3)));
        Assert.True(theme.FormatVersion >= 3000);
        var older = MetacityTheme.ParseFile(Path.Combine(eos, MetacityThemes.FileName(1)));
        Assert.Equal(1000, older.FormatVersion);
    }

    [Fact]
    public void Load_reads_v3_over_v1_from_a_data_directory()
    {
        using var data = new TemporaryDataHome();
        var theme = MetacityTheme.Load("eOS");
        Assert.True(theme.FormatVersion >= 3000);
        Assert.Equal(Path.Combine(data.Themes, "eOS", MetacityThemes.SubDirectory, MetacityThemes.FileName(3)), theme.FilePath);
        Assert.Contains("eOS", MetacityThemes.Available());
        Assert.Contains("Atlanta", MetacityThemes.Available());
    }

    [Fact]
    public void A_broken_v3_file_aborts_the_search_rather_than_falling_to_v2()
    {
        using var data = new TemporaryDataHome();
        var directory = Path.Combine(data.Themes, "eOS", MetacityThemes.SubDirectory);
        File.WriteAllText(Path.Combine(directory, MetacityThemes.FileName(3)), "<metacity_theme><bogus/></metacity_theme>");
        var error = Assert.Throws<MetacityThemeException>(() => MetacityTheme.Load("eOS"));
        Assert.Contains("<bogus> is not allowed below <metacity_theme>", error.Message);
    }

    [Fact]
    public void A_too_new_root_version_falls_through_to_the_older_file()
    {
        using var data = new TemporaryDataHome();
        var directory = Path.Combine(data.Themes, "eOS", MetacityThemes.SubDirectory);
        File.WriteAllText(Path.Combine(directory, MetacityThemes.FileName(3)), "<metacity_theme version=\">= 99.0\"><bogus/></metacity_theme>");
        var theme = MetacityTheme.Load("eOS");
        Assert.Equal(1000, theme.FormatVersion);
    }

    [Fact]
    public void A_missing_theme_names_the_directories_searched()
    {
        var error = Assert.Throws<MetacityThemeException>(() => MetacityTheme.Load("NoSuchThemeAnywhere"));
        Assert.Contains("NoSuchThemeAnywhere", error.Message);
        Assert.Contains(MetacityThemes.SubDirectory, error.Message);
    }

    [Fact]
    public void Version_in_a_v1_file_is_an_error()
    {
        var error = Assert.Throws<MetacityThemeException>(() => ParseText(Wrap("<constant version=\">= 3.1\" name=\"A\" value=\"1\"/>"), 1));
        Assert.Contains("\"version\" attribute cannot be used in metacity-theme-1.xml or metacity-theme-2.xml", error.Message);
        Assert.StartsWith("Line ", error.Message);
    }

    [Theory]
    [InlineData("<draw_ops name=\"t\"><title color=\"#fff\" x=\"0\" y=\"0\" ellipsize_width=\"width\"/></draw_ops>", 3000, "No \"ellipsize_width\" attribute on element <title>")]
    [InlineData("<window type=\"attached\" style_set=\"set\"/>", 3001, "Unknown type \"attached\"")]
    [InlineData("<frame_style name=\"s2\" parent=\"s\"><button function=\"left_single_background\" state=\"normal\" draw_ops=\"empty\"/></frame_style>", 3002, "Button function \"left_single_background\" does not exist in this version (3002, need 3003)")]
    [InlineData("<frame_style name=\"s2\" parent=\"s\"><button function=\"appmenu\" state=\"normal\" draw_ops=\"empty\"/></frame_style>", 3004, "Button function \"appmenu\" does not exist in this version (3004, need 3005)")]
    public void Each_three_x_gate_errors_below_its_version(string body, int version, string expected)
    {
        var text = Wrap(Geometry + "<draw_ops name=\"empty\"/>" + StyleFor(version) + body, $"version=\">= {version / 1000}.{version % 1000}\"");
        var error = Assert.Throws<MetacityThemeException>(() => ParseText(text, 3));
        Assert.Contains(expected, error.Message);
    }

    [Theory]
    [InlineData("<draw_ops name=\"t\"><title color=\"#fff\" x=\"0\" y=\"0\" ellipsize_width=\"width\"/></draw_ops>", 3001)]
    [InlineData("<window type=\"attached\" style_set=\"set\"/>", 3002)]
    [InlineData("<frame_style name=\"s2\" parent=\"s\"><button function=\"left_single_background\" state=\"normal\" draw_ops=\"empty\"/></frame_style>", 3003)]
    [InlineData("<frame_style name=\"s2\" parent=\"s\"><button function=\"appmenu\" state=\"normal\" draw_ops=\"empty\"/></frame_style>", 3005)]
    public void Each_three_x_gate_passes_at_its_version(string body, int version)
    {
        var text = Wrap(Geometry + "<draw_ops name=\"empty\"/>" + StyleFor(version) + body, $"version=\">= {version / 1000}.{version % 1000}\"");
        var theme = ParseText(text, 3);
        Assert.Equal(version, theme.FormatVersion);
    }

    [Fact]
    public void Unknown_elements_and_attributes_error_with_a_position()
    {
        var element = Assert.Throws<MetacityThemeException>(() => ParseText(Wrap("<bogus/>"), 1));
        Assert.Matches(@"^Line \d+ character \d+: Element <bogus> is not allowed below <metacity_theme>$", element.Message);
        var attribute = Assert.Throws<MetacityThemeException>(() => ParseText(Wrap("<constant name=\"A\" value=\"1\" bogus=\"2\"/>"), 1));
        Assert.Matches(@"^Line \d+ character \d+: Attribute ""bogus"" is invalid on <constant> element in this context$", attribute.Message);
    }

    [Fact]
    public void Ignored_elements_still_load()
    {
        var theme = ParseText(Wrap(Geometry + "<draw_ops name=\"empty\"/>" + MinimalStyle + "<menu_icon function=\"close\" state=\"normal\"><draw_ops><line color=\"#000\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\"/></draw_ops></menu_icon><fallback icon=\"x\"/>"), 1);
        Assert.Equal(1000, theme.FormatVersion);
    }

    [Fact]
    public void Lenient_v2_features_load_in_a_v1_file()
    {
        var text = Wrap(
            "<constant name=\"Two\" value=\"2\"/><constant name=\"Half\" value=\"0.5\"/><constant name=\"Ink\" value=\"#123456\"/>"
            + "<frame_geometry name=\"g\" rounded_top_left=\"7\" hide_buttons=\"true\"><distance name=\"left_width\" value=\"Two\"/><distance name=\"right_width\" value=\"1\"/><distance name=\"bottom_height\" value=\"1\"/><distance name=\"left_titlebar_edge\" value=\"1\"/><distance name=\"right_titlebar_edge\" value=\"1\"/><distance name=\"title_vertical_pad\" value=\"1\"/><border name=\"title_border\" left=\"1\" right=\"1\" top=\"1\" bottom=\"1\"/><border name=\"button_border\" left=\"1\" right=\"1\" top=\"1\" bottom=\"1\"/><aspect_ratio name=\"button\" value=\"1.0\"/></frame_geometry>"
            + "<draw_ops name=\"empty\"><arc color=\"Ink\" x=\"0\" y=\"0\" width=\"width\" height=\"height\" from=\"0\" to=\"180\"/></draw_ops>"
            + MinimalStyle.Replace("<frame_style name=\"s\" geometry=\"g\">", "<frame_style name=\"s\" geometry=\"g\" background=\"#fff\" alpha=\"0.5\">", StringComparison.Ordinal)
                .Replace("<frame focus=\"yes\" state=\"shaded\" style=\"s\"/>", "<frame focus=\"yes\" state=\"shaded\" resize=\"none\" style=\"s\"/>", StringComparison.Ordinal),
            string.Empty);
        var theme = ParseText(text, 1);
        Assert.Equal(7, theme.LookupLayout("g")!.TopLeftRadius);
        Assert.True(theme.LookupLayout("g")!.HideButtons);
        Assert.Equal(2, theme.LookupLayout("g")!.LeftWidth);
        Assert.True(theme.TryGetFloatConstant("Half", out var half));
        Assert.Equal(0.5, half);
    }

    [Fact]
    public void An_attribute_glued_to_the_previous_value_is_repaired_as_marco_reads_it()
    {
        var text = Wrap(Geometry + "<draw_ops name=\"empty\"/>" + MinimalStyle.Replace("<frame focus=\"yes\" state=\"normal\" resize=\"both\" style=\"s\"/>", "<frame focus=\"yes\"state=\"normal\" resize=\"both\" style=\"s\"/>", StringComparison.Ordinal));
        var theme = ParseText(text, 1);
        Assert.NotNull(theme.GetStyle(MetacityFrameType.Normal, MetacityFrameStateKind.Normal, MetacityResize.Both, MetacityFocus.Yes));
    }

    internal const string Geometry =
        "<frame_geometry name=\"g\"><distance name=\"left_width\" value=\"2\"/><distance name=\"right_width\" value=\"2\"/><distance name=\"bottom_height\" value=\"2\"/><distance name=\"left_titlebar_edge\" value=\"3\"/><distance name=\"right_titlebar_edge\" value=\"3\"/><distance name=\"button_width\" value=\"16\"/><distance name=\"button_height\" value=\"16\"/><distance name=\"title_vertical_pad\" value=\"4\"/><border name=\"title_border\" left=\"1\" right=\"1\" top=\"1\" bottom=\"1\"/><border name=\"button_border\" left=\"1\" right=\"1\" top=\"1\" bottom=\"1\"/></frame_geometry>";

    internal static string MinimalStyle => StyleFor(1000);

    internal static string StyleFor(int version)
    {
        var buttons = new List<string> { "close", "maximize", "minimize", "menu" };
        if (version >= 2000)
        {
            buttons.AddRange(["shade", "above", "stick", "unshade", "unabove", "unstick"]);
        }

        if (version >= 3005)
        {
            buttons.Add("appmenu");
        }

        var style = new System.Text.StringBuilder("<frame_style name=\"s\" geometry=\"g\">");
        foreach (var button in buttons)
        {
            style.Append($"<button function=\"{button}\" state=\"normal\" draw_ops=\"empty\"/><button function=\"{button}\" state=\"pressed\" draw_ops=\"empty\"/>");
        }

        style.Append("</frame_style>");
        return style + StyleSetAndWindows;
    }

    internal const string StyleSetAndWindows =
        "<frame_style_set name=\"set\"><frame focus=\"yes\" state=\"normal\" resize=\"both\" style=\"s\"/><frame focus=\"no\" state=\"normal\" resize=\"both\" style=\"s\"/><frame focus=\"yes\" state=\"maximized\" style=\"s\"/><frame focus=\"no\" state=\"maximized\" style=\"s\"/><frame focus=\"yes\" state=\"shaded\" style=\"s\"/><frame focus=\"no\" state=\"shaded\" style=\"s\"/><frame focus=\"yes\" state=\"maximized_and_shaded\" style=\"s\"/><frame focus=\"no\" state=\"maximized_and_shaded\" style=\"s\"/></frame_style_set>"
        + "<window type=\"normal\" style_set=\"set\"/><window type=\"dialog\" style_set=\"set\"/><window type=\"modal_dialog\" style_set=\"set\"/><window type=\"utility\" style_set=\"set\"/><window type=\"menu\" style_set=\"set\"/><window type=\"border\" style_set=\"set\"/>";

    internal static string Wrap(string body, string rootAttributes = "") =>
        $"<?xml version=\"1.0\"?><metacity_theme {rootAttributes}><info><name>t</name><author>a</author><copyright>c</copyright><date>d</date><description>e</description></info>{body}</metacity_theme>";

    internal static MetacityTheme ParseText(string text, int major)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
        return MetacityTheme.Parse(stream, FixtureRoot, major, "test");
    }

    internal sealed class TemporaryDataHome : IDisposable
    {
        private readonly string? _previousHome;
        private readonly string? _previousDirs;
        private readonly string _root;

        public TemporaryDataHome()
        {
            _root = Path.Combine(Path.GetTempPath(), $"basin-metacity-{Environment.ProcessId}-{Guid.NewGuid():N}");
            Themes = Path.Combine(_root, "themes");
            Directory.CreateDirectory(Themes);
            foreach (var theme in Directory.EnumerateDirectories(FixtureRoot))
            {
                CopyDirectory(theme, Path.Combine(Themes, Path.GetFileName(theme)));
            }

            _previousHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            _previousDirs = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", _root);
            Environment.SetEnvironmentVariable("XDG_DATA_DIRS", _root);
        }

        public string Themes { get; }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", _previousHome);
            Environment.SetEnvironmentVariable("XDG_DATA_DIRS", _previousDirs);
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (var file in Directory.EnumerateFiles(source))
            {
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
            }

            foreach (var directory in Directory.EnumerateDirectories(source))
            {
                CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
            }
        }
    }
}
