using TinyComp;
using Xunit;

namespace Basin.Tests;

public sealed class SettingsMessagesTests
{
    public static TheoryData<string, string, bool> Known => new()
    {
        { "effects.open: expected one of none, fade, zoom, glide, sheet, keeping none", "Choose none, fade, zoom, glide or sheet.", true },
        { "[canvas] window \"spin\" is not warp|scale|terrace, keeping warp", "Choose warp, scale or terrace.", true },
        { "compositor.transactions: expected true or false", "Use true or false.", true },
        { "canvas.mesh_cell: expected a whole number", "Enter a whole number.", true },
        { "[compositor] background \"orange\" is not #rrggbb, keeping the default", "Use a color like #1a2b3c.", true },
        { "[settings] font_size must be at least 6, keeping 14", "Use 6 or more.", true },
        { "a [[rule]] naming neither app_id nor title_regex is dropped", "A rule needs an app id or a title pattern.", true },
        { "unknown keysym 'Enterr' in binding 'Super+Enterr', skipping", "\"Enterr\" isn't a key name.", true },
        { "[canvas] edge_scale 0.9 must stay below zone / extension = 0.240, clamping it to 0.228", "Too high for this zone and extension, so 0.228 is used.", false },
        { "[canvas] min_scale 0.1 is below edge_scale 0.200 and has no effect", "This has no effect with the current edge scale.", false },
    };

    public static TheoryData<string> Every => new()
    {
        "effects.open: expected one of none, fade, zoom, keeping none",
        "compositor.scale: expected a number or an array of numbers",
        "frame.metacity.theme: expected a string",
        "unknown key 'effects.wobly', ignored",
        "unknown modifier 'Hyper' in binding 'Hyper+q', skipping",
        "binding 'Super+x' names no action and no command, skipping",
        "rule pattern '(' is invalid: Invalid pattern '(' at offset 1. Not enough )'s.",
        "[output.\"DP-1\"] mode \"big\" is not WIDTHxHEIGHT or WIDTHxHEIGHT@HZ, ignored",
        "[output.\"DP-1\"] transform \"sideways\" is not normal|90|180|270|flipped|flipped-90|flipped-180|flipped-270, ignored",
        "[output.\"DP-1\"] zone: unknown key or wrong type, ignored",
        "[frame] style = \"metacity\" names no theme in [frame.metacity] theme; installed themes: Atlanta, Menta",
        "[frame.metacity] theme = \"Nope\": no such theme; installed themes: Atlanta, Menta",
        "[color] source = \"icc\" names no profile in [color] icc, describing the outputs from their EDID",
        "effects.post: unknown stage 'blur', ignored",
        "effects.shader: an entry names no path, ignored",
        "[canvas] sides is empty: the canvas has no zones",
        "[shortcuts] \"toggle\" is not app_id:id, ignored",
        "[shortcuts] \"a:b\" names no chord, ignored",
        "[canvas] shelf_scale.left is not a number, ignored",
        "[overview] threshold_out 0.8 must be below threshold_in 0.6, swapping them",
        "[canvas] shelf 0.4 and zone 0.3 leave no flat center, scaling them to 0.250 and 0.200",
        "[canvas] extension 0.1 is below zone 0.12, raising it to 0.12",
        "[overview] gesture_fingers 3 is the workspace swipe's count, keeping 4",
        "[overview] shelf_scale on left is above scale 0.75: overview clamps it to 0.75",
        "[overview] is on and so is [canvas] enable: overview is off on every output with the canvas enabled",
        "[canvas] corner_radius 0.5 overrides corner",
        "[canvas] shelf_min_scale 0.5 is at or above shelf_scale on left: the fit shrink is off there",
        "effects.shader_params is ignored with [[effects.shader]]; put params on each entry",
        "[frame] font_size must be at least 1, keeping 14",
        "something new that no rule knows about, ignored",
    };

    [Theory]
    [MemberData(nameof(Known))]
    public void A_known_warning_reads_as_a_short_sentence(string raw, string text, bool blocking)
    {
        var explained = SettingsMessages.Explain(raw);
        Assert.Equal(text, explained.Text);
        Assert.Equal(blocking, explained.Blocking);
    }

    [Theory]
    [MemberData(nameof(Every))]
    public void No_warning_shows_config_syntax_or_log_wording(string raw)
    {
        var text = SettingsMessages.Explain(raw).Text;
        Assert.DoesNotContain("[", text, StringComparison.Ordinal);
        Assert.DoesNotContain("|", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ignored", text, StringComparison.Ordinal);
        Assert.DoesNotContain("keeping", text, StringComparison.Ordinal);
        Assert.DoesNotContain("_", text.Replace("app_id:id", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.True(text.Length <= 80, $"{text.Length} characters: {text}");
        Assert.True(char.IsUpper(text[0]) || text[0] == '"', text);
        Assert.EndsWith(".", text, StringComparison.Ordinal);
    }
}
