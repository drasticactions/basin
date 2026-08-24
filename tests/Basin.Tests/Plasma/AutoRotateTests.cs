using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Basin.Plasma;
using Xunit;

namespace Basin.Tests;

public sealed class AutoRotateTests
{
    private sealed class FakeOrientation : IOrientationSource
    {
        public bool Available { get; set; }

        public OutputTransform? Reading { get; set; }

        public bool Enabled { get; private set; }

        public bool IsAvailable => Available;

        public OutputTransform? Orientation => Reading;

        public event Action? Changed;

        public void SetEnabled(bool enabled) => Enabled = enabled;

        public void Raise() => Changed?.Invoke();
    }

    private static (AutoRotateOutputConfiguration Configuration, FakeOrientation Sensor, TestEdidOutput Panel, OutputLayout Layout) Compose()
    {
        var layout = new OutputLayout();
        var panel = new TestEdidOutput("eDP-1", []);
        layout.Add(panel, 0, 0);
        var sensor = new FakeOrientation { Available = true };
        var configuration = new AutoRotateOutputConfiguration(new LayoutOutputConfiguration(layout), sensor);
        return (configuration, sensor, panel, layout);
    }

    [Fact]
    public void The_bit_needs_the_sensor_and_an_internal_connector()
    {
        var (configuration, sensor, panel, layout) = Compose();
        var external = new TestEdidOutput("DP-1", []);
        layout.Add(external, 640, 0);

        Assert.True((configuration.Supported(panel) & OutputConfigurationFeatures.AutoRotate) != 0);
        Assert.True((configuration.Supported(external) & OutputConfigurationFeatures.AutoRotate) == 0);

        sensor.Available = false;
        Assert.True((configuration.Supported(panel) & OutputConfigurationFeatures.AutoRotate) == 0);

        panel.Destroy();
        external.Destroy();
    }

    [Fact]
    public void An_always_policy_follows_the_sensor_and_never_restores_the_manual_transform()
    {
        var (configuration, sensor, panel, _) = Compose();
        sensor.Reading = OutputTransform.Rotate90;

        Assert.True(configuration.Apply(
        [
            new OutputConfigurationEntry
            {
                Output = panel,
                Enabled = true,
                AutoRotate = OutputAutoRotatePolicy.Always,
            },
        ]));
        Assert.Equal(OutputTransform.Rotate90, panel.Transform);
        Assert.True(sensor.Enabled);
        Assert.True(configuration.TryRead(panel, out var state));
        Assert.Equal(OutputAutoRotatePolicy.Always, state.AutoRotate);

        sensor.Reading = OutputTransform.Rotate180;
        sensor.Raise();
        Assert.Equal(OutputTransform.Rotate180, panel.Transform);

        Assert.True(configuration.Apply(
        [
            new OutputConfigurationEntry
            {
                Output = panel,
                Enabled = true,
                AutoRotate = OutputAutoRotatePolicy.Never,
            },
        ]));
        Assert.Equal(OutputTransform.Normal, panel.Transform);
        Assert.False(sensor.Enabled);

        panel.Destroy();
    }

    [Fact]
    public void The_tablet_mode_policy_gates_on_the_switch()
    {
        var (configuration, sensor, panel, _) = Compose();
        sensor.Reading = OutputTransform.Rotate270;

        Assert.True(configuration.Apply(
        [
            new OutputConfigurationEntry
            {
                Output = panel,
                Enabled = true,
                Transform = OutputTransform.Rotate180,
                AutoRotate = OutputAutoRotatePolicy.InTabletMode,
            },
        ]));
        Assert.Equal(OutputTransform.Rotate180, panel.Transform);
        Assert.False(sensor.Enabled);

        configuration.TabletMode = true;
        Assert.Equal(OutputTransform.Rotate270, panel.Transform);
        Assert.True(sensor.Enabled);

        configuration.TabletMode = false;
        Assert.Equal(OutputTransform.Rotate180, panel.Transform);
        Assert.False(sensor.Enabled);

        panel.Destroy();
    }
}
