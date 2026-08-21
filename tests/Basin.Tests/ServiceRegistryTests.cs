using Basin.Capabilities;
using Basin.Capabilities.Defaults;
using Basin.Desktop;
using Xunit;

namespace Basin.Tests;

public sealed class ServiceRegistryTests
{
    [Fact]
    public void Use_after_freeze_throws()
    {
        using var host = new CompositorTestHost();
        using var services = new BasinServices(host.Display, host.Loop).Freeze();

        Assert.Throws<InvalidOperationException>(() => services.Use<IBell>(SilentBell.Instance));
        Assert.Throws<InvalidOperationException>(() => services.Install(new SystemBellModule()));
    }

    [Fact]
    public void Registering_the_same_capability_twice_throws()
    {
        using var host = new CompositorTestHost();
        using var services = new BasinServices(host.Display, host.Loop);
        services.Use<IBell>(new SilentBell());

        Assert.Throws<InvalidOperationException>(() => services.Use<IBell>(new SilentBell()));
    }

    [Fact]
    public void A_consumer_registration_wins_whichever_side_of_install_it_is_on()
    {
        using var host = new CompositorTestHost();
        var before = new SilentBell();
        var after = new SilentBell();

        using (var services = new BasinServices(host.Display, host.Loop))
        {
            services.Use(host.Compositor).Use<IBell>(before).Install(new SystemBellModule()).Freeze();
            Assert.Same(before, services.Find<IBell>());
        }

        using (var services = new BasinServices(host.Display, host.Loop))
        {
            services.Install(new SystemBellModule()).Use(host.Compositor).Use<IBell>(after).Freeze();
            Assert.Same(after, services.Find<IBell>());
        }
    }

    [Fact]
    public void An_unclaimed_capability_is_reported_and_the_global_still_installs()
    {
        using var host = new CompositorTestHost();
        using var services = new BasinServices(host.Display, host.Loop)
            .Install(new ScreencopyModule())
            .Use(host.Layout)
            .Use(host.Buffers)
            .Freeze();

        Assert.Contains(typeof(IScreenCapture), services.UnresolvedCapabilities);
        Assert.True(services.Modules.ContainsKey("zwlr_screencopy_manager_v1"));
    }

    [Fact]
    public void A_capability_a_module_brought_is_not_reported_unresolved()
    {
        using var host = new CompositorTestHost();
        using var services = new BasinServices(host.Display, host.Loop)
            .Use(host.Layout)
            .Install(new OutputManagementModule())
            .Freeze();

        Assert.DoesNotContain(typeof(IOutputConfiguration), services.UnresolvedCapabilities);
        Assert.IsType<LayoutOutputConfiguration>(services.Find<IOutputConfiguration>());
    }

    [Fact]
    public void Subtraction_removes_the_global_entirely()
    {
        using var host = new CompositorTestHost();
        using var services = new BasinServices(host.Display, host.Loop)
            .Use(host.Layout)
            .Use(host.Compositor)
            .Use(host.Buffers)
            .Use<IBell>(SilentBell.Instance)
            .Without("zwlr_screencopy_manager_v1")
            .Install(new ScreencopyModule())
            .Install(new SystemBellModule())
            .Freeze();

        Assert.False(services.Modules.ContainsKey("zwlr_screencopy_manager_v1"));
        Assert.True(services.Modules.ContainsKey("xdg_system_bell_v1"));
        Assert.Null(services.Module<ScreencopyModule>());
    }

    [Fact]
    public void Two_modules_claiming_one_wire_interface_throw()
    {
        using var host = new CompositorTestHost();
        using var services = new BasinServices(host.Display, host.Loop);
        services.Install(new SystemBellModule());

        Assert.Throws<InvalidOperationException>(() => services.Install(new SystemBellModule()));
        Assert.Throws<ArgumentException>(() => new ProtocolPack([new SystemBellModule(), new SystemBellModule()]));
    }

    [Fact]
    public void Explicit_sync_is_not_advertised_without_a_device()
    {
        using var host = new CompositorTestHost();
        using var services = new BasinServices(host.Display, host.Loop)
            .Use(host.Compositor)
            .Install(new LinuxDrmSyncobjModule())
            .Freeze();

        Assert.False(services.Modules.ContainsKey("wp_linux_drm_syncobj_manager_v1"));
        Assert.Contains(typeof(IDrmSyncDevice), services.UnresolvedCapabilities);
    }

    [Fact]
    public void Disposal_retires_globals_and_leaves_the_counters_where_it_found_them()
    {
        LeakTracking.Require();
        using var host = new CompositorTestHost();
        var before = Diagnostics.BasinCounters.LiveObjects;

        var services = new BasinServices(host.Display, host.Loop)
            .Use(host.Layout)
            .Use(host.Compositor)
            .Use(host.Buffers)
            .Use<IBell>(new SilentBell())
            .Install(new SystemBellModule())
            .Install(new ScreencopyModule())
            .Install(new WorkspaceModule())
            .Freeze();
        services.Dispose();

        Assert.Equal(before, Diagnostics.BasinCounters.LiveObjects);
    }

    [Fact]
    public void The_whole_pack_installs_onto_a_bare_display()
    {
        using var host = new CompositorTestHost();

        using var services = new BasinServices(host.Display, host.Loop)
            .Use(new OutputLayout())
            .Use(host.Compositor)
            .Use(host.Buffers)
            .Use(host.Seat)
            .Use<IFrameClock>(new Basin.Capabilities.Defaults.FrameClock())
            .Use<IActivationTokens>(new DefaultActivationTokens())
            .Use<IBell>(SilentBell.Instance)
            .Install(DesktopPack.Desktop)
            .Freeze();

        Assert.NotNull(services.Require<Basin.Desktop.ScreencopyManager>());
        Assert.NotNull(services.Require<Basin.Desktop.WorkspaceManager>());
        Assert.NotNull(services.Find<IWorkspaceModel>());
        Assert.NotNull(services.Find<IActivationTokens>());
        Assert.NotNull(services.Find<IBell>());
    }
}

public sealed class ShmVersionTests
{
    [Fact]
    public void The_reported_version_is_the_one_clients_are_advertised()
    {
        using var host = new CompositorTestHost();

        var advertised = 0u;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "wl_shm")
            {
                advertised = e.Version;
            }
        };
        host.PumpToClient();

        Assert.NotEqual(0u, advertised);
        Assert.Equal((int)advertised, new ShmModule().Version);
    }
}
