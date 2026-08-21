using Basin;
using Basin.Desktop;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class ForeignToplevelTests
{
    [Fact]
    public void A_change_that_leaves_the_identity_alone_sends_nothing()
    {
        using var host = new CompositorTestHost();
        var model = new TestToplevelModel();
        using var manager = new ForeignToplevelManager(host.Display, model);

        var titles = 0;
        var appIds = 0;
        var dones = 0;
        var proxy = Bind<Basin.Desktop.Protocol.ZwlrForeignToplevelManagerV1>(
            host, "zwlr_foreign_toplevel_manager_v1", ForeignToplevelManager.Version);
        proxy.Toplevel += (_, e) =>
        {
            e.Toplevel.Title += (_, _) => titles++;
            e.Toplevel.AppId += (_, _) => appIds++;
            e.Toplevel.Done += (_, _) => dones++;
        };

        var id = model.Add("a title", "an.app.id");
        host.PumpUntil(() => dones > 0);

        var titlesAfterAdd = titles;
        var appIdsAfterAdd = appIds;
        var donesAfterAdd = dones;

        for (var i = 0; i < 20; i++)
        {
            model.Reposition(id, new Box(i, i, 100, 100));
        }

        host.PumpToClient();

        Assert.Equal(titlesAfterAdd, titles);
        Assert.Equal(appIdsAfterAdd, appIds);
        Assert.Equal(donesAfterAdd, dones);

        model.Retitle(id, "another title");
        host.PumpUntil(() => dones > donesAfterAdd);

        Assert.Equal(titlesAfterAdd + 1, titles);
        Assert.Equal(appIdsAfterAdd, appIds);
    }

    [Fact]
    public void The_toplevel_list_leaves_the_identity_alone_too()
    {
        using var host = new CompositorTestHost();
        var model = new TestToplevelModel();
        using var manager = new ForeignToplevelListManager(host.Display, model);

        var titles = 0;
        var appIds = 0;
        var dones = 0;
        var proxy = Bind<Basin.Desktop.Protocol.ExtForeignToplevelListV1>(
            host, "ext_foreign_toplevel_list_v1", ForeignToplevelListManager.Version);
        proxy.Toplevel += (_, e) =>
        {
            e.Toplevel.Title += (_, _) => titles++;
            e.Toplevel.AppId += (_, _) => appIds++;
            e.Toplevel.Done += (_, _) => dones++;
        };

        var id = model.Add("a title", "an.app.id");
        host.PumpUntil(() => dones > 0);

        var titlesAfterAdd = titles;
        var donesAfterAdd = dones;

        for (var i = 0; i < 20; i++)
        {
            model.Reposition(id, new Box(i, i, 100, 100));
        }

        host.PumpToClient();

        Assert.Equal(titlesAfterAdd, titles);
        Assert.Equal(donesAfterAdd, dones);

        model.Retitle(id, "another title");
        host.PumpUntil(() => dones > donesAfterAdd);

        Assert.Equal(titlesAfterAdd + 1, titles);
    }

    [Fact]
    public void Reporting_a_change_to_nobody_allocates_nothing()
    {
        using var host = new CompositorTestHost();
        var model = new TestToplevelModel();
        using var wlr = new ForeignToplevelManager(host.Display, model);
        using var ext = new ForeignToplevelListManager(host.Display, model);

        var id = model.Add("a title", "an.app.id");
        for (var i = 0; i < 20; i++)
        {
            model.Reposition(id, new Box(i, i, 100, 100));
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            model.Reposition(id, new Box(i, i, 100, 100));
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
    }

    private static T Bind<T>(CompositorTestHost host, string wireInterface, int version)
        where T : WlProxy, IWaylandObject<T>
    {
        T? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == wireInterface)
            {
                proxy = registry.Bind<T>(e.Name, (uint)version);
            }
        };

        host.PumpToClient();
        Assert.NotNull(proxy);
        return proxy!;
    }
}
