using Xunit;

namespace Basin.Tests;

public sealed class DamAccessibilityTests
{
    [Fact]
    public async Task Super_alt_8_zooms_and_super_alt_c_draws_the_screen_curtain_in_the_kiosk()
    {
        using var dam = DamInspectorProcessTests.DamProcess.StartWith("pixman", ["sleep", "120"]);
        void AccessChord(int key)
        {
            foreach (var (code, pressed) in new[] { (125, 1), (56, 1), (key, 1), (key, 0), (56, 0), (125, 0) })
            {
                dam.Send($"key {code} {pressed}");
            }
        }

        AccessChord(9);
        Assert.NotNull(await dam.WaitFor(line => line == "ZOOM factor=2"));
        AccessChord(46);
        Assert.NotNull(await dam.WaitFor(line => line == "CURTAIN on"));
        AccessChord(46);
        Assert.NotNull(await dam.WaitFor(line => line == "CURTAIN off"));
        AccessChord(12);
        Assert.NotNull(await dam.WaitFor(line => line == "ZOOM factor=1.6"));
    }
}
