using Basin.Freedesktop;
using Xunit;

namespace Basin.Tests;

public sealed class ExecLineTests
{
    [Fact]
    public void Split_follows_the_quoting_rules_and_drops_the_field_codes()
    {
        Assert.Equal(["/opt/My App/bin", "--flag=\"x\""], ExecLine.Split("\"/opt/My App/bin\" \"--flag=\\\"x\\\"\" %U"));
        Assert.Equal(["fooview"], ExecLine.Split("fooview %F"));
        Assert.Equal(["fooview", "--open"], ExecLine.Split("fooview --open %f %i %c %k"));
        Assert.Equal(["printf", "100%"], ExecLine.Split("printf 100%%"));
        Assert.Equal(["a", "", "b"], ExecLine.Split("a \"\" b"));
        Assert.Equal(["echo", "a`b$c\\d"], ExecLine.Split("echo \"a\\`b\\$c\\\\d\""));
        Assert.Equal(["echo", "%U"], ExecLine.Split("echo \"%U\""));
        Assert.Equal(["sh", "-c", "exec foo"], ExecLine.Split("sh -c \"exec foo\""));
        Assert.Empty(ExecLine.Split(""));
        Assert.Empty(ExecLine.Split("%U"));
        Assert.Equal(["trailing%"], ExecLine.Split("trailing%"));
    }

    [Fact]
    public void Join_quotes_what_the_shell_and_the_spec_reserve_and_round_trips()
    {
        Assert.Equal("fooview --open", ExecLine.Join(["fooview", "--open"]));
        Assert.Equal("\"/opt/My App/bin\" \"--flag=\\\"x\\\"\"", ExecLine.Join(["/opt/My App/bin", "--flag=\"x\""]));
        Assert.Equal("\"\"", ExecLine.Join([""]));
        Assert.Equal("\"a\\`b\\$c\\\\d\"", ExecLine.Join(["a`b$c\\d"]));
        Assert.Equal("\"it's\"", ExecLine.Join(["it's"]));
        string[] argv = ["/opt/My App/bin", "--flag=\"x\"", "$HOME", "plain", "", "a;b", "*.txt"];
        Assert.Equal(argv, ExecLine.Split(ExecLine.Join(argv)));
    }

    [Fact]
    public void The_terminal_is_TERMINAL_with_dash_e_then_xdg_terminal_exec_on_PATH_then_nothing()
    {
        var previous = (Environment.GetEnvironmentVariable("TERMINAL"), Environment.GetEnvironmentVariable("PATH"));
        var bin = Path.Combine(Path.GetTempPath(), "basin-terminal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(bin);
        try
        {
            Environment.SetEnvironmentVariable("PATH", bin);
            Environment.SetEnvironmentVariable("TERMINAL", "foot --app-id=\"my term\"");
            Assert.Equal(["foot", "--app-id=my term", "-e"], ExecLine.TerminalFromEnvironment()!);
            Environment.SetEnvironmentVariable("TERMINAL", "  ");
            Assert.Null(ExecLine.TerminalFromEnvironment());
            Environment.SetEnvironmentVariable("TERMINAL", null);
            Assert.Null(ExecLine.TerminalFromEnvironment());
            File.WriteAllText(Path.Combine(bin, "xdg-terminal-exec"), "");
            Assert.Equal(["xdg-terminal-exec"], ExecLine.TerminalFromEnvironment()!);
            Environment.SetEnvironmentVariable("TERMINAL", "kitty");
            Assert.Equal(["kitty", "-e"], ExecLine.TerminalFromEnvironment()!);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TERMINAL", previous.Item1);
            Environment.SetEnvironmentVariable("PATH", previous.Item2);
            Directory.Delete(bin, recursive: true);
        }
    }
}
