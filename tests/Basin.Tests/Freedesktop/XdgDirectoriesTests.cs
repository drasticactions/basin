using Basin.Freedesktop;
using Xunit;

namespace Basin.Tests;

public sealed class XdgDirectoriesTests
{
    [Fact]
    public void State_home_reads_an_absolute_variable_and_falls_back_otherwise()
    {
        var saved = Environment.GetEnvironmentVariable("XDG_STATE_HOME");
        var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state");
        try
        {
            Environment.SetEnvironmentVariable("XDG_STATE_HOME", "/tmp/basin-state");
            Assert.Equal("/tmp/basin-state", XdgDirectories.StateHome);
            Environment.SetEnvironmentVariable("XDG_STATE_HOME", "relative/state");
            Assert.Equal(fallback, XdgDirectories.StateHome);
            Environment.SetEnvironmentVariable("XDG_STATE_HOME", null);
            Assert.Equal(fallback, XdgDirectories.StateHome);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_STATE_HOME", saved);
        }
    }
}
