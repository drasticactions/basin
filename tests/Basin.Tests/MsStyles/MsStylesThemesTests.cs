using Basin.Themes.MsStyles;
using Xunit;

namespace Basin.Tests;

public sealed class MsStylesThemesTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("msstyles-themes-").FullName;
    private readonly Dictionary<string, string?> _saved = [];

    public MsStylesThemesTests()
    {
        Set("HOME", Path.Combine(_root, "home"));
        Set("XDG_DATA_HOME", Path.Combine(_root, "data"));
        Set("XDG_DATA_DIRS", Path.Combine(_root, "system"));
        Set("WINEPREFIX", Path.Combine(_root, "wine"));
    }

    [Fact]
    public void A_theme_under_the_metacity_roots_is_found_by_its_file_or_as_the_only_file()
    {
        var named = Write(Path.Combine(_root, "home", ".themes", "Luna"), "Luna.msstyles", MsStylesWriter.Plain());
        var single = Write(Path.Combine(_root, "data", "themes", "Royale"), "royale-variant.msstyles", MsStylesWriter.Plain());

        Assert.Equal(named, MsStylesThemes.Find("Luna"));
        Assert.Equal(named, MsStylesThemes.Find("luna"));
        Assert.Equal(single, MsStylesThemes.Find("Royale"));
    }

    [Fact]
    public void Two_styles_in_one_theme_directory_need_the_named_file()
    {
        Write(Path.Combine(_root, "home", ".themes", "Pair"), "one.msstyles", MsStylesWriter.Plain());
        Write(Path.Combine(_root, "home", ".themes", "Pair"), "two.msstyles", MsStylesWriter.Plain());

        Assert.Null(MsStylesThemes.Find("Pair"));
    }

    [Fact]
    public void A_theme_under_msstyles_is_laid_out_as_windows_resources_themes()
    {
        var path = Write(Path.Combine(_root, "system", "msstyles", "Aero"), "Aero.msstyles", MsStylesWriter.Glassy());

        Assert.Equal(path, MsStylesThemes.Find("aero"));
    }

    [Fact]
    public void A_wine_prefix_is_searched_without_regard_to_case()
    {
        var path = Write(Path.Combine(_root, "wine", "drive_c", "Windows", "Resources", "Themes", "Luna"), "luna.MSSTYLES", MsStylesWriter.Plain());

        Assert.Equal(path, MsStylesThemes.Find("Luna"));
    }

    [Fact]
    public void The_metacity_roots_win_over_the_others()
    {
        var themes = Write(Path.Combine(_root, "home", ".themes", "Luna"), "Luna.msstyles", MsStylesWriter.Plain());
        Write(Path.Combine(_root, "data", "msstyles", "Luna"), "Luna.msstyles", MsStylesWriter.Plain());

        Assert.Equal(themes, MsStylesThemes.Find("Luna"));
    }

    [Fact]
    public void A_value_with_a_slash_is_a_path()
    {
        var path = Write(Path.Combine(_root, "anywhere"), "Mine.msstyles", MsStylesWriter.Plain());

        Assert.Equal(path, MsStylesThemes.Find(path));
        Assert.Null(MsStylesThemes.Find(Path.Combine(_root, "missing.msstyles")));
        Assert.Null(MsStylesThemes.Find("Missing"));
    }

    [Fact]
    public void Available_lists_each_theme_once_with_its_colors_and_sizes()
    {
        Write(Path.Combine(_root, "home", ".themes", "Luna"), "Luna.msstyles", MsStylesWriter.Plain());
        Write(Path.Combine(_root, "data", "msstyles", "luna"), "luna.msstyles", MsStylesWriter.Plain());
        Write(Path.Combine(_root, "wine", "drive_c", "windows", "resources", "themes", "Aero"), "Aero.msstyles", MsStylesWriter.Glassy());
        Write(Path.Combine(_root, "data", "msstyles", "Broken"), "Broken.msstyles", [1, 2, 3]);

        var available = MsStylesThemes.Available();

        Assert.Equal(["Aero", "Luna"], available.Select(t => t.Name));
        var luna = available.Single(t => t.Name == "Luna");
        Assert.Equal(MsStylesWriter.PlainColors, luna.Colors);
        Assert.Equal(MsStylesWriter.PlainSizes, luna.Sizes);
    }

    [Fact]
    public void Available_names_match_a_full_load()
    {
        var paths = new[]
        {
            Write(Path.Combine(_root, "data", "msstyles", "Plain"), "Plain.msstyles", MsStylesWriter.Plain()),
            Write(Path.Combine(_root, "data", "msstyles", "Glassy"), "Glassy.msstyles", MsStylesWriter.Glassy()),
            Write(Path.Combine(_root, "data", "msstyles", "Variant"), "Variant.msstyles", MsStylesWriter.PlainAsV4()),
        };

        var available = MsStylesThemes.Available();

        Assert.Equal(paths.Length, available.Count);
        foreach (var path in paths)
        {
            var entry = available.Single(t => Path.GetFullPath(t.Path) == Path.GetFullPath(path));
            var theme = MsStylesTheme.Load(path);
            Assert.Equal(theme.ColorSchemes, entry.Colors);
            Assert.Equal(theme.Sizes, entry.Sizes);
            Assert.NotEmpty(entry.Colors);
        }
    }

    public void Dispose()
    {
        foreach (var (name, value) in _saved)
        {
            Environment.SetEnvironmentVariable(name, value);
        }

        Directory.Delete(_root, recursive: true);
    }

    private void Set(string name, string value)
    {
        _saved[name] = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    private static string Write(string directory, string file, byte[] bytes)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, file);
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
