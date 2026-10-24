using Basin.Tests.ImGui;
using Xunit;

namespace Basin.Tests;

public sealed class InletAccessibilityTests
{
    [Fact]
    public async Task Super_alt_8_zooms_and_super_alt_c_draws_the_screen_curtain_with_no_window_manager()
    {
        using var inlet = InletInspectorTests.InletProcess.Start("pixman");
        async Task AccessChord(int key)
        {
            foreach (var (code, pressed) in new[] { (125, 1), (56, 1), (key, 1), (key, 0), (56, 0), (125, 0) })
            {
                await inlet.SendAsync($"key {code} {pressed}");
            }
        }

        await AccessChord(9);
        Assert.NotNull(await inlet.WaitForAsync(line => line == "ZOOM factor=2"));
        await AccessChord(46);
        Assert.NotNull(await inlet.WaitForAsync(line => line == "CURTAIN on"));
        await AccessChord(46);
        Assert.NotNull(await inlet.WaitForAsync(line => line == "CURTAIN off"));
        await AccessChord(12);
        Assert.NotNull(await inlet.WaitForAsync(line => line == "ZOOM factor=1.6"));
        await inlet.QuitAsync();
    }
}
