using Basin.Config;
using Basin.Host;
using Xunit;

namespace Basin.Tests;

public sealed class AccessibilityGateTests
{
    [Theory]
    [InlineData(AccessibilityMode.Auto, HostBackend.Drm, false, true)]
    [InlineData(AccessibilityMode.Auto, HostBackend.Nested, false, false)]
    [InlineData(AccessibilityMode.Auto, HostBackend.Headless, false, false)]
    [InlineData(AccessibilityMode.Auto, HostBackend.Headless, true, true)]
    [InlineData(AccessibilityMode.On, HostBackend.Nested, false, true)]
    [InlineData(AccessibilityMode.Off, HostBackend.Drm, true, false)]
    public void Auto_is_on_for_a_full_session_and_on_or_off_force_it(AccessibilityMode mode, HostBackend backend, bool greeter, bool on)
    {
        Assert.Equal(on, AccessibilityGate.IsOn(mode, new HostOptions { Backend = backend, Greeter = greeter }, out var reason));
        Assert.False(string.IsNullOrEmpty(reason));
    }

    [Theory]
    [InlineData("", AccessibilityMode.Auto)]
    [InlineData("[accessibility]\nenabled = \"on\"\n", AccessibilityMode.On)]
    [InlineData("[accessibility]\nenabled = \"off\"\n", AccessibilityMode.Off)]
    [InlineData("[accessibility]\nenabled = \"maybe\"\n", AccessibilityMode.Auto)]
    public void The_enabled_key_reads_auto_on_and_off_and_falls_back_to_auto(string toml, AccessibilityMode expected)
    {
        var reader = new TomlReader(Tomlyn.Toml.ToModel(toml), Basin.Diagnostics.BasinLog.For("test"));
        Assert.Equal(expected, AccessibilitySettings.Read(reader.Section("accessibility")).Enabled);
    }

    [Theory]
    [InlineData("on", AccessibilityMode.Off, HostBackend.Headless, true)]
    [InlineData("off", AccessibilityMode.On, HostBackend.Drm, false)]
    [InlineData("auto", AccessibilityMode.On, HostBackend.Nested, false)]
    [InlineData("maybe", AccessibilityMode.On, HostBackend.Nested, true)]
    public void The_environment_override_beats_the_configured_mode(string value, AccessibilityMode mode, HostBackend backend, bool on)
    {
        var saved = Environment.GetEnvironmentVariable(AccessibilityGate.OverrideVariable);
        Environment.SetEnvironmentVariable(AccessibilityGate.OverrideVariable, value);
        try
        {
            Assert.Equal(on, AccessibilityGate.IsOn(mode, new HostOptions { Backend = backend }, out var reason));
            Assert.False(string.IsNullOrEmpty(reason));
        }
        finally
        {
            Environment.SetEnvironmentVariable(AccessibilityGate.OverrideVariable, saved);
        }
    }
}
