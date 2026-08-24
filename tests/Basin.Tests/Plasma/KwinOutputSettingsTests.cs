using System.Security.Cryptography;
using Basin.Capabilities;
using Basin.Plasma;
using Xunit;

namespace Basin.Tests;

public sealed class KwinOutputSettingsTests
{
    private sealed class StoredOutput : OutputBase
    {
        private readonly byte[] _edid;

        public StoredOutput(string name, byte[] edid, OutputConfigurationFeatures features)
            : base(name)
        {
            _edid = edid;
            Features = features;
            using var initial = new OutputState();
            Commit(initial.SetEnabled(true).SetMode(new OutputMode(1920, 1080, 60_000)));
        }

        public override OutputConfigurationFeatures Features { get; }

        public override ReadOnlyMemory<byte> EdidBytes => _edid;

        protected override bool TestCommitCore(OutputState state) => true;

        protected override bool CommitCore(OutputState state) => true;
    }

    private sealed class RecordingConfiguration : IOutputConfiguration
    {
        public OutputConfigurationFeatures Features;
        public bool AcceptModes = true;
        public readonly List<IReadOnlyList<OutputConfigurationEntry>> Applications = [];

        public event Action<IReadOnlyList<OutputConfigurationEntry>>? Applied;

        public bool Test(IReadOnlyList<OutputConfigurationEntry> entries)
        {
            if (AcceptModes)
            {
                return true;
            }

            foreach (var entry in entries)
            {
                if (entry.Mode is not null)
                {
                    return false;
                }
            }

            return true;
        }

        public bool Apply(IReadOnlyList<OutputConfigurationEntry> entries)
        {
            if (!Test(entries))
            {
                return false;
            }

            Applications.Add(entries);
            Applied?.Invoke(entries);
            return true;
        }

        public OutputConfigurationFeatures Supported(IOutput output) => Features;
    }

    private static byte[] Edid(ushort product, uint serial, byte week, byte year)
    {
        var edid = new byte[128];
        edid[0] = 0x00;
        for (var i = 1; i < 7; i++)
        {
            edid[i] = 0xff;
        }

        var vendor = ((('H' - 'A' + 1) & 0x1f) << 10) | ((('W' - 'A' + 1) & 0x1f) << 5) | (('V' - 'A' + 1) & 0x1f);
        edid[8] = (byte)(vendor >> 8);
        edid[9] = (byte)vendor;
        edid[10] = (byte)product;
        edid[11] = (byte)(product >> 8);
        edid[12] = (byte)serial;
        edid[13] = (byte)(serial >> 8);
        edid[14] = (byte)(serial >> 16);
        edid[15] = (byte)(serial >> 24);
        edid[16] = week;
        edid[17] = year;
        return edid;
    }

    private static string Hash(byte[] edid) => Convert.ToHexStringLower(MD5.HashData(edid));

    private static string File(string outputs, string setups) =>
        $$"""
        [
            { "name": "outputs", "data": [ {{outputs}} ] },
            { "name": "setups", "data": [ {{setups}} ] }
        ]
        """;

    [Fact]
    public void The_stored_row_supplies_scale_mode_and_transform()
    {
        var edid = Edid(28194, 3229677090, 16, 32);
        var settings = Parse(File(
            $$"""
            {
                "connectorName": "DP-1",
                "edidIdentifier": "HWV 28194 3229677090 16 2022 0",
                "edidHash": "{{Hash(edid)}}",
                "scale": 1.7,
                "transform": "Rotated90",
                "mode": { "width": 3840, "height": 2560, "refreshRate": 59984, "flags": 1 }
            }
            """,
            """
            { "lidClosed": false, "outputs": [
                { "enabled": true, "outputIndex": 0, "position": { "x": 100, "y": 50 }, "priority": 0 } ] }
            """));

        var output = new StoredOutput("DP-1", edid, OutputConfigurationFeatures.None);
        var entries = settings.EntriesFor([output]);

        var entry = Assert.Single(entries);
        Assert.Equal(1.7, entry.Scale);
        Assert.Equal(new OutputMode(3840, 2560, 59984), entry.Mode);
        Assert.Equal(OutputTransform.Rotate90, entry.Transform);
        Assert.Equal(new Point(100, 50), entry.Position);
        Assert.True(entry.Enabled);
        Assert.Equal(0u, entry.Priority);
        output.Destroy();
    }

    [Fact]
    public void An_output_the_file_does_not_name_is_left_alone()
    {
        var stored = Edid(28194, 3229677090, 16, 32);
        var settings = Parse(File(
            $$"""
            { "connectorName": "DP-1", "edidIdentifier": "HWV 28194 3229677090 16 2022 0",
              "edidHash": "{{Hash(stored)}}", "scale": 1.7 }
            """,
            """
            { "lidClosed": false, "outputs": [
                { "enabled": true, "outputIndex": 0, "position": { "x": 0, "y": 0 }, "priority": 0 } ] }
            """));

        var output = new StoredOutput("HDMI-A-1", Edid(4242, 1, 5, 30), OutputConfigurationFeatures.None);
        Assert.Empty(settings.EntriesFor([output]));
        output.Destroy();
    }

    [Fact]
    public void A_connector_name_matches_when_the_row_carries_no_edid()
    {
        var settings = Parse(File(
            """
            { "connectorName": "HEADLESS-1", "scale": 2 }
            """,
            """
            { "lidClosed": false, "outputs": [
                { "enabled": true, "outputIndex": 0, "position": { "x": 0, "y": 0 }, "priority": 0 } ] }
            """));

        var output = new StoredOutput("HEADLESS-1", [], OutputConfigurationFeatures.None);
        var entry = Assert.Single(settings.EntriesFor([output]));
        Assert.Equal(2, entry.Scale);
        output.Destroy();
    }

    [Fact]
    public void A_setup_disables_and_places_the_second_output()
    {
        var first = Edid(1, 1, 1, 30);
        var second = Edid(2, 2, 2, 30);
        var settings = Parse(File(
            $$"""
            { "connectorName": "DP-1", "edidHash": "{{Hash(first)}}", "scale": 1 },
            { "connectorName": "DP-2", "edidHash": "{{Hash(second)}}", "scale": 1 }
            """,
            """
            { "lidClosed": false, "outputs": [
                { "enabled": true, "outputIndex": 0, "position": { "x": 0, "y": 0 }, "priority": 0 },
                { "enabled": false, "outputIndex": 1, "position": { "x": 1920, "y": 0 }, "priority": 1 } ] }
            """));

        var left = new StoredOutput("DP-1", first, OutputConfigurationFeatures.None);
        var right = new StoredOutput("DP-2", second, OutputConfigurationFeatures.None);
        var entries = settings.EntriesFor([left, right]);

        Assert.Equal(2, entries.Count);
        Assert.True(entries[0].Enabled);
        Assert.False(entries[1].Enabled);
        Assert.Equal(new Point(1920, 0), entries[1].Position);
        left.Destroy();
        right.Destroy();
    }

    [Fact]
    public void Fractions_become_the_protocol_units()
    {
        var edid = Edid(7, 7, 7, 30);
        var settings = Parse(File(
            $$"""
            { "connectorName": "DP-1", "edidHash": "{{Hash(edid)}}",
              "brightness": 0.5, "sharpness": 0.25, "sdrGamutWideness": 1,
              "sdrBrightness": 496, "minBrightnessOverride": 0.005,
              "maxPeakBrightnessOverride": 1000, "maxAverageBrightnessOverride": 400 }
            """,
            """
            { "lidClosed": false, "outputs": [
                { "enabled": true, "outputIndex": 0, "position": { "x": 0, "y": 0 }, "priority": 0 } ] }
            """));

        var output = new StoredOutput("DP-1", edid, OutputConfigurationFeatures.None);
        var entry = Assert.Single(settings.EntriesFor([output]));

        Assert.Equal(5000u, entry.Brightness);
        Assert.Equal(2500u, entry.Sharpness);
        Assert.Equal(10000u, entry.SdrGamutWideness);
        Assert.Equal(496u, entry.SdrBrightnessNits);
        Assert.Equal(new OutputBrightnessOverrides(1000, 400, 50), entry.BrightnessOverrides);
        output.Destroy();
    }

    [Fact]
    public void An_unsupported_field_is_dropped_rather_than_refused()
    {
        var edid = Edid(9, 9, 9, 30);
        var settings = Parse(File(
            $$"""
            { "connectorName": "DP-1", "edidHash": "{{Hash(edid)}}",
              "scale": 1.5, "highDynamicRange": true, "sharpness": 1 }
            """,
            """
            { "lidClosed": false, "outputs": [
                { "enabled": true, "outputIndex": 0, "position": { "x": 0, "y": 0 }, "priority": 0 } ] }
            """));

        var output = new StoredOutput("DP-1", edid, OutputConfigurationFeatures.None);
        var configuration = new RecordingConfiguration();
        Assert.True(settings.Apply(configuration, new IOutput[] { output }));

        var entry = Assert.Single(Assert.Single(configuration.Applications));
        Assert.Equal(1.5, entry.Scale);
        Assert.Null(entry.HighDynamicRange);
        Assert.Null(entry.Sharpness);

        var capable = new RecordingConfiguration
        {
            Features = OutputConfigurationFeatures.HighDynamicRange | OutputConfigurationFeatures.Sharpness,
        };
        Assert.True(settings.Apply(capable, new IOutput[] { output }));
        var kept = Assert.Single(Assert.Single(capable.Applications));
        Assert.True(kept.HighDynamicRange);
        Assert.Equal(10000u, kept.Sharpness);
        output.Destroy();
    }

    [Fact]
    public void A_mode_the_configuration_refuses_costs_the_mode_and_not_the_scale()
    {
        var edid = Edid(11, 11, 11, 30);
        var settings = Parse(File(
            $$"""
            { "connectorName": "DP-1", "edidHash": "{{Hash(edid)}}", "scale": 2,
              "mode": { "width": 5120, "height": 2880, "refreshRate": 60000, "flags": 1 } }
            """,
            """
            { "lidClosed": false, "outputs": [
                { "enabled": true, "outputIndex": 0, "position": { "x": 0, "y": 0 }, "priority": 0 } ] }
            """));

        var output = new StoredOutput("DP-1", edid, OutputConfigurationFeatures.None);
        var configuration = new RecordingConfiguration { AcceptModes = false };
        Assert.True(settings.Apply(configuration, new IOutput[] { output }));

        var entry = Assert.Single(Assert.Single(configuration.Applications));
        Assert.Null(entry.Mode);
        Assert.Equal(2, entry.Scale);
        output.Destroy();
    }

    [Fact]
    public void A_cvt_mode_reads_as_its_visible_size_and_rate()
    {
        var edid = Edid(13, 13, 13, 30);
        var settings = Parse(File(
            $$"""
            { "connectorName": "DP-1", "edidHash": "{{Hash(edid)}}", "mode": { "flags": 1, "cvt": {
                "clock": 148500, "hdisplay": 1920, "hsyncStart": 2008, "hsyncEnd": 2052, "htotal": 2200,
                "hskew": 0, "vdisplay": 1080, "vsyncStart": 1084, "vsyncEnd": 1089, "vtotal": 1125,
                "vscan": 0, "flags": 5 } } }
            """,
            """
            { "lidClosed": false, "outputs": [
                { "enabled": true, "outputIndex": 0, "position": { "x": 0, "y": 0 }, "priority": 0 } ] }
            """));

        var output = new StoredOutput("DP-1", edid, OutputConfigurationFeatures.None);
        var entry = Assert.Single(settings.EntriesFor([output]));
        Assert.Equal(new OutputMode(1920, 1080, 60_000), entry.Mode);
        output.Destroy();
    }

    [Fact]
    public void A_file_without_both_groups_is_refused()
    {
        Assert.False(KwinOutputSettings.TryParse("[ { \"name\": \"outputs\", \"data\": [] } ]", out _));
        Assert.False(KwinOutputSettings.TryParse("not json", out _));
        Assert.False(KwinOutputSettings.TryParse("[]", out _));
    }

    private static KwinOutputSettings Parse(string json)
    {
        Assert.True(KwinOutputSettings.TryParse(json, out var settings));
        return settings;
    }
}
