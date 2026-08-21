using Basin.Desktop;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class PresentationHintTests
{
    [Fact]
    public void Tearing_and_content_type_hints_round_trip()
    {
        using var host = new CompositorTestHost();
        using var tearing = new TearingControlManager(host.Display, host.Compositor);
        using var contentType = new ContentTypeManager(host.Display, host.Compositor);
        var window = MappedToplevel.Map(host, host.Client);

        Basin.Desktop.Protocol.WpTearingControlManagerV1? tearingProxy = null;
        Basin.Desktop.Protocol.WpContentTypeManagerV1? contentProxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            switch (e.Interface)
            {
                case "wp_tearing_control_manager_v1":
                    tearingProxy = registry.Bind<Basin.Desktop.Protocol.WpTearingControlManagerV1>(e.Name, 1);
                    break;
                case "wp_content_type_manager_v1":
                    contentProxy = registry.Bind<Basin.Desktop.Protocol.WpContentTypeManagerV1>(e.Name, 1);
                    break;
            }
        };
        host.PumpToClient();
        Assert.NotNull(tearingProxy);
        Assert.NotNull(contentProxy);

        var hints = new List<bool>();
        tearing.HintChanged += (_, prefers) => hints.Add(prefers);
        var types = new List<ContentTypeManager.ContentType>();
        contentType.TypeChanged += (_, type) => types.Add(type);

        var control = tearingProxy!.GetTearingControl(window.Surface);
        control.SetPresentationHint(Basin.Desktop.Protocol.WpTearingControlV1.PresentationHint.Async);
        var typed = contentProxy!.GetSurfaceContentType(window.Surface);
        typed.SetContentType(Basin.Desktop.Protocol.WpContentTypeV1.Type.Game);
        host.PumpUntil(() => hints.Count == 1 && types.Count == 1);

        Assert.True(tearing.PrefersTearing(window.ServerSurface));
        Assert.Equal(ContentTypeManager.ContentType.Game, contentType.TypeOf(window.ServerSurface));

        control.Dispose();
        typed.Dispose();
        host.PumpUntil(() => hints.Count == 2 && types.Count == 2);
        Assert.False(tearing.PrefersTearing(window.ServerSurface));
        Assert.Equal(ContentTypeManager.ContentType.None, contentType.TypeOf(window.ServerSurface));
        Assert.False(hints[1]);
        host.PumpToServer();
    }
}
