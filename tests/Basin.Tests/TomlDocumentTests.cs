using System.Runtime.CompilerServices;
using Basin.Config;
using Tomlyn;
using Xunit;

namespace Basin.Tests;

public sealed class TomlDocumentTests
{
    public static TheoryData<string> Corpus => new()
    {
        "samples/TinyComp/tinycomp.toml",
        "samples/DeskbarWM/deskbar-wm.toml",
        "samples/MauiComp/maui-comp.toml",
    };

    [Theory]
    [MemberData(nameof(Corpus))]
    public void A_shipped_config_round_trips_byte_for_byte(string relative)
    {
        var text = File.ReadAllText(Path.Combine(Root(), relative));

        Assert.Equal(text, Toml.Parse(text).ToString());
        Assert.Equal(text, TomlDocument.Parse(text).ToString());
    }

    [Fact]
    public void A_file_that_does_not_parse_is_refused()
    {
        Assert.Throws<FormatException>(() => TomlDocument.Parse("[compositor\nrenderer = \n"));
    }

    [Fact]
    public void An_edit_that_defines_a_key_twice_is_refused_and_leaves_the_text_alone()
    {
        const string Text = "[effects]\nshader = \"none\"\n";
        var document = TomlDocument.Parse(Text);

        Assert.Throws<InvalidOperationException>(() => document.AppendTable("effects.shader"));
        Assert.Equal(Text, document.ToString());
    }

    [Fact]
    public void Setting_an_existing_key_keeps_its_comment_and_column()
    {
        var document = TomlDocument.Parse("[compositor]\nrenderer      = \"vulkan\"     # --renderer\noutputs       = 1            # --outputs\n");

        document.Set("compositor", "renderer", TomlValue.From("gl"));
        document.Set("compositor", "outputs", TomlValue.From(2));

        Assert.Equal(
            "[compositor]\nrenderer      = \"gl\"     # --renderer\noutputs       = 2            # --outputs\n",
            document.ToString());
    }

    [Fact]
    public void An_absent_key_goes_after_the_table_s_last_line_and_before_its_trailing_comments()
    {
        var document = TomlDocument.Parse("[canvas]\nedge_scale = 0.15   # scale\n\n# about overview\n[overview]\nzoom = 1\n");

        document.Set("canvas", "zone", TomlValue.From(24));

        Assert.Equal(
            "[canvas]\nedge_scale = 0.15   # scale\nzone = 24\n\n# about overview\n[overview]\nzoom = 1\n",
            document.ToString());
    }

    [Fact]
    public void An_absent_table_is_appended_at_the_end()
    {
        var document = TomlDocument.Parse("[compositor]\noutputs = 1\n");

        document.Set("settings", "palette", TomlValue.From("light"));

        Assert.Equal("[compositor]\noutputs = 1\n\n[settings]\npalette = \"light\"\n", document.ToString());
    }

    [Fact]
    public void A_commented_out_example_stays_a_comment()
    {
        const string Text = "[frame]\nstyle = \"flat\"\n\n# [frame.metacity]\n# theme = \"Menta\"\n\n[color]\nhdr = false\n";
        var document = TomlDocument.Parse(Text);

        document.Set("frame.metacity", "theme", TomlValue.From("Atlanta"));

        Assert.Equal(Text + "\n[frame.metacity]\ntheme = \"Atlanta\"\n", document.ToString());
        Assert.True(document.Contains("frame.metacity", "theme"));
    }

    [Fact]
    public void Removing_a_key_takes_its_line_and_comment_and_leaves_the_comment_above()
    {
        var document = TomlDocument.Parse("[effects]\n# wobbly windows\nwobbly = true   # on\nfade = true\n");

        Assert.True(document.Remove("effects", "wobbly"));
        Assert.False(document.Remove("effects", "wobbly"));

        Assert.Equal("[effects]\n# wobbly windows\nfade = true\n", document.ToString());
    }

    [Fact]
    public void Quoted_and_dotted_table_names_match_however_they_are_spelled()
    {
        var document = TomlDocument.Parse("[output.\"DP-1\"]\nscale = 1.5\n\n[ output . 'HDMI-A-1' ]\nscale = 1\n");

        document.Set("output.\"DP-1\"", "scale", TomlValue.From(2.0));
        document.Set("output.HDMI-A-1", "transform", TomlValue.From("90"));
        document.Set("output.\"eDP 1\"", "scale", TomlValue.From(1.25));

        Assert.Equal(
            "[output.\"DP-1\"]\nscale = 2.0\n\n[ output . 'HDMI-A-1' ]\nscale = 1\ntransform = \"90\"\n\n[output.\"eDP 1\"]\nscale = 1.25\n",
            document.ToString());
    }

    [Fact]
    public void Inline_tables_and_arrays_are_written_and_replaced()
    {
        var document = TomlDocument.Parse("[canvas]\nshelf_scale = 0.4   # one number\n[compositor]\nscale = []\n");

        document.Set("canvas", "shelf_scale", TomlValue.Inline([new("left", TomlValue.From(0.4)), new("right", TomlValue.From(0.5))]));
        document.Set("compositor", "scale", TomlValue.Array([TomlValue.From(1.5), TomlValue.From(2.0)]));
        document.Set("bindings", "Super+Return", TomlValue.Inline([new("exec", TomlValue.From("foot"))]));

        Assert.Equal(
            "[canvas]\nshelf_scale = { left = 0.4, right = 0.5 }   # one number\n[compositor]\nscale = [1.5, 2.0]\n\n[bindings]\n\"Super+Return\" = { exec = \"foot\" }\n",
            document.ToString());
        Assert.Equal("{ exec = \"foot\" }", document.RawValue("bindings", "Super+Return"));
    }

    [Fact]
    public void Arrays_of_tables_are_listed_edited_appended_removed_and_reordered()
    {
        const string Text = "[compositor]\noutputs = 1\n\n[[rule]]\napp_id = \"foot\"\n\n# mpv\n[[rule]]\napp_id = \"mpv\"\nframe = false\n\n[[rule]]\ntitle_regex = \"^x\"\n";
        var document = TomlDocument.Parse(Text);
        Assert.Equal(3, document.TableCount("rule"));

        document.SetInTable("rule", 1, "frame", TomlValue.From(true));
        document.SetInTable("rule", 0, "workspace", TomlValue.From(2));
        Assert.True(document.RemoveInTable("rule", 2, "title_regex"));
        Assert.Equal(
            "[compositor]\noutputs = 1\n\n[[rule]]\napp_id = \"foot\"\nworkspace = 2\n\n# mpv\n[[rule]]\napp_id = \"mpv\"\nframe = true\n\n[[rule]]\n",
            document.ToString());

        Assert.Equal(3, document.AppendTable("rule"));
        document.SetInTable("rule", 3, "app_id", TomlValue.From("kitty"));
        document.RemoveTable("rule", 2);
        Assert.Equal(
            "[compositor]\noutputs = 1\n\n[[rule]]\napp_id = \"foot\"\nworkspace = 2\n\n# mpv\n[[rule]]\napp_id = \"mpv\"\nframe = true\n\n[[rule]]\napp_id = \"kitty\"\n",
            document.ToString());

        document.MoveTable("rule", 2, 0);
        Assert.Equal("\"kitty\"", document.RawValueInTable("rule", 0, "app_id"));
        Assert.Equal("\"foot\"", document.RawValueInTable("rule", 1, "app_id"));
        Assert.Equal("\"mpv\"", document.RawValueInTable("rule", 2, "app_id"));

        document.MoveTable("rule", 0, 2);
        Assert.Equal("\"foot\"", document.RawValueInTable("rule", 0, "app_id"));
        Assert.Equal("\"mpv\"", document.RawValueInTable("rule", 1, "app_id"));
        Assert.Equal("\"kitty\"", document.RawValueInTable("rule", 2, "app_id"));
        Assert.Contains("# mpv", document.ToString(), StringComparison.Ordinal);
        Toml.ToModel(document.ToString());
    }

    [Fact]
    public void Two_edits_to_the_seeded_file_change_exactly_two_lines()
    {
        var text = File.ReadAllText(Path.Combine(Root(), "samples/TinyComp/tinycomp.toml"));
        var document = TomlDocument.Parse(text);

        document.Set("effects", "wobbly", TomlValue.From(true));
        document.Set("canvas", "edge_scale", TomlValue.From(0.15));

        var before = text.Split('\n');
        var after = document.ToString().Split('\n');
        Assert.Equal(before.Length, after.Length);
        var changed = new List<string>();
        for (var i = 0; i < before.Length; i++)
        {
            if (before[i] != after[i])
            {
                changed.Add(after[i]);
                Assert.Equal(CommentOf(before[i]), CommentOf(after[i]));
            }
        }

        Assert.Equal(2, changed.Count);
        Assert.StartsWith("wobbly", changed[0], StringComparison.Ordinal);
        Assert.StartsWith("edge_scale", changed[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Saving_writes_through_a_symlink_keeps_the_mode_and_leaves_no_temporary()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var directory = Directory.CreateTempSubdirectory("basin-toml-");
        try
        {
            var target = Path.Combine(directory.FullName, "real.toml");
            File.WriteAllText(target, "[a]\nb = 1\n");
            File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var link = Path.Combine(directory.FullName, "link.toml");
            File.CreateSymbolicLink(link, target);

            var document = TomlDocument.Load(link);
            document.Set("a", "b", TomlValue.From(2));
            document.Save(link);

            Assert.Equal("[a]\nb = 2\n", File.ReadAllText(target));
            Assert.NotNull(new FileInfo(link).LinkTarget);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(target));
            Assert.Equal(["link.toml", "real.toml"], Directory.GetFiles(directory.FullName).Select(Path.GetFileName).Order());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Keys_and_table_names_list_what_the_file_carries_in_order()
    {
        var document = TomlDocument.Parse("[bindings]\n\"Alt+Tab\" = \"cycle\"\nquit = false\n\n[output.\"DP-1\"]\nscale = 2\n[[rule]]\napp_id = \"x\"\n");

        Assert.Equal(["Alt+Tab", "quit"], document.Keys("bindings"));
        Assert.Empty(document.Keys("nothing"));
        Assert.Equal(2, document.TableNames().Count);
        Assert.Equal(["output", "DP-1"], document.TableNames()[1]);
    }

    [Fact]
    public void A_literal_parses_only_when_it_is_one_toml_value()
    {
        Assert.Equal("{ exec = \"foot\" }", TomlValue.Parse(" { exec = \"foot\" } ").Text);
        Assert.Equal("0.15", TomlValue.Parse("0.15").Text);
        Assert.False(TomlValue.TryParse("beos", out _));
        Assert.False(TomlValue.TryParse("1\nx = 2", out _));
        Assert.False(TomlValue.TryParse(string.Empty, out _));
    }

    private static string CommentOf(string line)
    {
        var hash = line.IndexOf('#', StringComparison.Ordinal);
        return hash < 0 ? string.Empty : line[hash..];
    }

    private static string Root([CallerFilePath] string sourcePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, "..", ".."));
}
