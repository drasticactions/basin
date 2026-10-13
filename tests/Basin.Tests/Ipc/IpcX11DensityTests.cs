using System.Text.Json;
using Basin.Capabilities;
using Basin.Ipc;
using Xunit;

namespace Basin.Tests;

public sealed class IpcX11DensityTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("basin-x11-density-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private static JsonElement Parse(string frame) => JsonDocument.Parse(frame).RootElement.Clone();

    private static string? ErrorCode(string frame) =>
        Parse(frame).TryGetProperty("error", out var error) ? error.GetProperty("code").GetString() : null;

    private static string Json(string text) => "\"" + System.Text.Encodings.Web.JavaScriptEncoder.Default.Encode(text) + "\"";

    [Fact]
    public void An_X11_window_reports_its_display_and_density_and_a_Wayland_window_omits_both()
    {
        using var rig = IpcFullRig.Create();
        var model = (TestToplevelModel)rig.Services.Require<IToplevelModel>();
        var wayland = model.Add("foot", "foot");
        var x11 = model.Add("Leafpad", "Leafpad");
        model.SetX11(x11, ":2", X11Density.Low);
        var peer = rig.Connect();

        var windows = Parse(peer.Call("""{"method":"windows/list"}""")).GetProperty("result").GetProperty("windows").EnumerateArray().ToArray();
        var waylandWindow = windows.Single(w => w.GetProperty("id").GetUInt64() == wayland);
        var x11Window = windows.Single(w => w.GetProperty("id").GetUInt64() == x11);
        Assert.False(waylandWindow.TryGetProperty("x11_display", out _));
        Assert.False(waylandWindow.TryGetProperty("x11_density", out _));
        Assert.Equal(":2", x11Window.GetProperty("x11_display").GetString());
        Assert.Equal("low", x11Window.GetProperty("x11_density").GetString());

        var single = Parse(peer.Call($$$"""{"method":"windows/get","params":{"id":{{{x11}}}}}""")).GetProperty("result");
        Assert.Equal("low", single.GetProperty("x11_density").GetString());
    }

    private sealed class FakeDensityControl : IX11DensityControl
    {
        public List<(ulong Id, X11Density Density)> Calls { get; } = [];

        public bool TrySet(ulong toplevelId, X11Density density, Action<X11DensityResult> completed, out string? error)
        {
            Calls.Add((toplevelId, density));
            if (toplevelId == 1)
            {
                error = "window 1 is not an X11 window";
                return false;
            }

            error = null;
            completed(new X11DensityResult(X11DensityOutcome.Relaunched, "exec=xeyes x11=low-density", null, 9));
            return true;
        }
    }

    [Fact]
    public void Set_x11_density_answers_the_outcome_and_refuses_what_the_control_refuses()
    {
        var control = new FakeDensityControl();
        using var rig = IpcFullRig.Create(services: services => services.Use<IX11DensityControl>(control));
        var model = (TestToplevelModel)rig.Services.Require<IToplevelModel>();
        _ = model.Add("foot", "foot");
        var x11 = model.Add("xeyes", "XEyes");
        var peer = rig.Connect();

        var result = Parse(peer.Call($$$"""{"method":"windows/set-x11-density","params":{"id":{{{x11}}},"density":"low"}}""")).GetProperty("result");
        Assert.Equal("relaunched", result.GetProperty("outcome").GetString());
        Assert.Equal("exec=xeyes x11=low-density", result.GetProperty("rule").GetString());
        Assert.Equal(9, result.GetProperty("launch_id").GetInt64());
        Assert.False(result.TryGetProperty("reason", out _));
        Assert.Equal([(x11, X11Density.Low)], control.Calls);

        Assert.Equal(IpcErrorCodes.Refused, ErrorCode(peer.Call("""{"method":"windows/set-x11-density","params":{"id":1,"density":"native"}}""")));
        Assert.Equal(IpcErrorCodes.InvalidParams, ErrorCode(peer.Call($$$"""{"method":"windows/set-x11-density","params":{"id":{{{x11}}},"density":"half"}}""")));
        Assert.NotNull(ErrorCode(peer.Call("""{"method":"windows/set-x11-density","params":{"id":999,"density":"low"}}""")));
    }

    [Fact]
    public void Set_x11_density_is_absent_without_a_control()
    {
        using var rig = new IpcTestRig((services, host) =>
        {
            services.Use(host.Layout);
            services.Use<IToplevelModel>(new TestToplevelModel());
        });
        _ = rig.Connect();
        Assert.True(rig.Server.Methods.Contains(IpcMethodNames.WindowsList));
        Assert.False(rig.Server.Methods.Contains(IpcMethodNames.WindowsSetX11Density));
    }

    [Fact]
    public void A_spawn_with_a_density_takes_its_route_and_an_unknown_density_is_refused()
    {
        using var rig = IpcFullRig.Create();
        rig.Server.Processes.X11Routes = density => density == X11Density.Low
            ? new X11Route(":77", new Dictionary<string, string> { ["XCURSOR_SIZE"] = "24" })
            : null;
        var peer = rig.Connect();
        _ = peer.Call("""{"method":"ipc/subscribe","params":{"events":["process/exited"]}}""");
        var log = Path.Combine(_directory, "display.log");
        var launch = Parse(peer.Call($$$"""{"method":"process/spawn","params":{"argv":["sh","-c","echo $DISPLAY $XCURSOR_SIZE"],"x11_density":"low","log":{{{Json(log)}}}}}"""))
            .GetProperty("result").GetProperty("launch_id").GetInt64();
        Assert.Equal("process/exited", Parse(peer.Receive(5000)).GetProperty("event").GetString());
        var tail = Parse(peer.Call($$$"""{"method":"process/log","params":{"launch_id":{{{launch}}}}}""")).GetProperty("result");
        Assert.Equal([":77 24"], tail.GetProperty("lines").EnumerateArray().Select(l => l.GetString()));

        Assert.Equal(IpcErrorCodes.InvalidParams, ErrorCode(peer.Call("""{"method":"process/spawn","params":{"argv":["true"],"x11_density":"half"}}""")));
    }
}
