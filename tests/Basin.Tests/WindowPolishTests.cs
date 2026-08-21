using Basin.Desktop;
using Basin.Shell.Xdg;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class ExtDataControlTests
{
    [Fact]
    public void Clipboard_and_primary_round_trip()
    {
        using var host = new CompositorTestHost();
        using var manager = new ExtDataControlManager(host.Display, new Basin.Seat.SeatSelectionStore(host.Seat));

        Basin.Desktop.Protocol.ExtDataControlManagerV1? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "ext_data_control_manager_v1")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.ExtDataControlManagerV1>(e.Name, 1);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);

        var device = proxy!.GetDataDevice(host.Client.Seat!);
        var offeredMimes = new List<string>();
        var selections = 0;
        var primaries = 0;
        device.DataOffer += (_, e) => e.Id.Offer += (_, oe) => offeredMimes.Add(oe.MimeType);
        device.Selection += (_, e) =>
        {
            if (e.Id is not null)
            {
                selections++;
            }
        };
        device.PrimarySelection += (_, e) =>
        {
            if (e.Id is not null)
            {
                primaries++;
            }
        };
        host.PumpToServer();

        var source = proxy.CreateDataSource();
        source.Offer("text/plain");
        source.Offer("text/html");
        device.SetSelection(source);
        host.PumpUntil(() => selections == 1);
        Assert.Contains("text/plain", offeredMimes);
        Assert.Contains("text/html", offeredMimes);

        var primarySource = proxy.CreateDataSource();
        primarySource.Offer("text/plain");
        device.SetPrimarySelection(primarySource);
        host.PumpUntil(() => primaries == 1);

        Assert.NotNull(host.Seat.DataDevice.Selection);
        Assert.NotNull(host.Seat.DataDevice.PrimarySelection);
        Assert.Equal(2, host.Seat.DataDevice.Selection!.MimeTypes.Count);

        device.Dispose();
        host.PumpToServer();
    }
}

public sealed class AlphaModifierTests
{
    [Fact]
    public void Multiplier_applies_and_resets_on_destroy()
    {
        using var host = new CompositorTestHost();
        using var manager = new AlphaModifierManager(host.Display, host.Compositor);
        var window = MappedToplevel.Map(host, host.Client);

        Basin.Desktop.Protocol.WpAlphaModifierV1? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "wp_alpha_modifier_v1")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.WpAlphaModifierV1>(e.Name, 1);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);

        var changes = new List<double>();
        manager.AlphaChanged += (_, alpha) => changes.Add(alpha);

        var modifier = proxy!.GetSurface(window.Surface);
        modifier.SetMultiplier(uint.MaxValue / 2);
        host.PumpUntil(() => changes.Count == 1);
        Assert.Equal(0.5, manager.AlphaOf(window.ServerSurface), 2);

        modifier.Dispose();
        host.PumpUntil(() => changes.Count == 2);
        Assert.Equal(1.0, manager.AlphaOf(window.ServerSurface));

        host.PumpToServer();
    }
}

public sealed class BackgroundEffectTests
{
    [Fact]
    public void Blur_region_is_double_buffered_and_persists_across_commits()
    {
        using var host = new CompositorTestHost();
        using var manager = new BackgroundEffectManager(host.Display, host.Compositor, new BlurCapable());
        var window = MappedToplevel.Map(host, host.Client);

        uint? capabilities = null;
        var proxy = Bind(host, p => p.Capabilities += (_, e) => capabilities = (uint)e.Flags);
        host.PumpUntil(() => capabilities is not null);
        Assert.Equal(1u, capabilities);

        var effect = proxy.GetBackgroundEffect(window.Surface);
        var region = host.Client.Compositor.CreateRegion();
        region.Add(10, 20, 30, 40);
        effect.SetBlurRegion(region);
        region.Destroy();
        host.PumpToServer();

        Assert.Null(manager.BlurRegionOf(window.ServerSurface));

        window.Surface.Commit();
        host.PumpToServer();
        Assert.Equal((10, 20, 40, 60), Extents(manager.BlurRegionOf(window.ServerSurface)!));

        window.Surface.Commit();
        host.PumpToServer();
        Assert.Equal((10, 20, 40, 60), Extents(manager.BlurRegionOf(window.ServerSurface)!));

        effect.SetBlurRegion(null);
        host.PumpToServer();
        Assert.Equal((10, 20, 40, 60), Extents(manager.BlurRegionOf(window.ServerSurface)!));
        window.Surface.Commit();
        host.PumpToServer();
        Assert.Equal((0, 0, 0, 0), Extents(manager.BlurRegionOf(window.ServerSurface)!));

        effect.Destroy();
        host.PumpToServer();
    }

    [Fact]
    public void Destroying_the_effect_object_clears_the_region_on_the_next_commit()
    {
        using var host = new CompositorTestHost();
        using var manager = new BackgroundEffectManager(host.Display, host.Compositor, new BlurCapable());
        var window = MappedToplevel.Map(host, host.Client);

        var proxy = Bind(host);
        var effect = proxy.GetBackgroundEffect(window.Surface);
        var region = host.Client.Compositor.CreateRegion();
        region.Add(0, 0, 5, 5);
        effect.SetBlurRegion(region);
        region.Destroy();
        window.Surface.Commit();
        host.PumpToServer();
        Assert.Equal((0, 0, 5, 5), Extents(manager.BlurRegionOf(window.ServerSurface)!));

        effect.Destroy();
        host.PumpToServer();
        Assert.Equal((0, 0, 5, 5), Extents(manager.BlurRegionOf(window.ServerSurface)!));

        window.Surface.Commit();
        host.PumpToServer();
        Assert.Equal((0, 0, 0, 0), Extents(manager.BlurRegionOf(window.ServerSurface)!));

        var second = proxy.GetBackgroundEffect(window.Surface);
        host.PumpToServer();
        second.Destroy();
        host.PumpToServer();
    }

    [Fact]
    public void Second_background_effect_object_is_a_protocol_error()
    {
        using var host = new CompositorTestHost();
        using var manager = new BackgroundEffectManager(host.Display, host.Compositor, new BlurCapable());
        var window = MappedToplevel.Map(host, host.Client);

        var proxy = Bind(host);
        var first = proxy.GetBackgroundEffect(window.Surface);
        host.PumpToServer();
        proxy.GetBackgroundEffect(window.Surface);
        host.PumpToServer();

        var error = Assert.Throws<WaylandProtocolException>(host.PumpToClient);
        Assert.Contains("ext_background_effect_manager_v1", error.Message, StringComparison.Ordinal);
        GC.KeepAlive(first);
    }

    [Fact]
    public void Set_blur_region_after_the_surface_died_is_a_protocol_error()
    {
        using var host = new CompositorTestHost();
        using var manager = new BackgroundEffectManager(host.Display, host.Compositor, new BlurCapable());

        var surface = host.Client.Compositor.CreateSurface();
        var proxy = Bind(host);
        var effect = proxy.GetBackgroundEffect(surface);
        host.PumpToServer();

        surface.Destroy();
        host.PumpToServer();

        effect.SetBlurRegion(null);
        host.PumpToServer();

        var error = Assert.Throws<WaylandProtocolException>(host.PumpToClient);
        Assert.Contains("ext_background_effect_surface_v1", error.Message, StringComparison.Ordinal);
    }

    private static Basin.Desktop.Protocol.ExtBackgroundEffectManagerV1 Bind(
        CompositorTestHost host,
        Action<Basin.Desktop.Protocol.ExtBackgroundEffectManagerV1>? wire = null)
    {
        Basin.Desktop.Protocol.ExtBackgroundEffectManagerV1? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "ext_background_effect_manager_v1")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.ExtBackgroundEffectManagerV1>(e.Name, 1);

                wire?.Invoke(proxy);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);
        return proxy!;
    }

    private static (int, int, int, int) Extents(Pixman.PixmanRegion32 region)
    {
        var extents = region.Extents;
        return (extents.X1, extents.Y1, extents.X2, extents.Y2);
    }

    private sealed class BlurCapable : Basin.Capabilities.IBackgroundEffects
    {
        public Basin.Capabilities.BackgroundEffects Supported => Basin.Capabilities.BackgroundEffects.Blur;
    }
}

public sealed class XdgForeignTests
{
    [Fact]
    public void Export_import_parent_and_invalid_handles()
    {
        using var host = new CompositorTestHost();
        using var manager = new XdgForeignManager(host.Display, host.Compositor);
        var parents = new List<(Surface Child, Surface Parent)>();
        manager.ParentRequested += (child, parent) => parents.Add((child, parent));

        var exportedWindow = MappedToplevel.Map(host, host.Client, color: 0xFF111111);
        var childWindow = MappedToplevel.Map(host, host.Client, color: 0xFF222222);

        Basin.Desktop.Protocol.ZxdgExporterV2? exporter = null;
        Basin.Desktop.Protocol.ZxdgImporterV2? importer = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            switch (e.Interface)
            {
                case "zxdg_exporter_v2":
                    exporter = registry.Bind<Basin.Desktop.Protocol.ZxdgExporterV2>(e.Name, 1);
                    break;
                case "zxdg_importer_v2":
                    importer = registry.Bind<Basin.Desktop.Protocol.ZxdgImporterV2>(e.Name, 1);
                    break;
            }
        };
        host.PumpToClient();
        Assert.NotNull(exporter);
        Assert.NotNull(importer);

        var exported = exporter!.ExportToplevel(exportedWindow.Surface);
        string? handle = null;
        exported.Handle += (_, e) => handle = e.Handle;
        host.PumpUntil(() => handle is not null);

        var imported = importer!.ImportToplevel(handle!);
        var importDied = false;
        imported.Destroyed += (_, _) => importDied = true;
        imported.SetParentOf(childWindow.Surface);
        host.PumpUntil(() => parents.Count == 1);
        Assert.Same(childWindow.ServerSurface, parents[0].Child);
        Assert.Same(exportedWindow.ServerSurface, parents[0].Parent);

        exported.Dispose();
        host.PumpUntil(() => importDied);

        var bogus = importer.ImportToplevel("no-such-handle");
        var bogusDied = false;
        bogus.Destroyed += (_, _) => bogusDied = true;
        host.PumpUntil(() => bogusDied);

        imported.Dispose();
        bogus.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void Handles_cross_the_two_revisions()
    {
        using var host = new CompositorTestHost();
        using var manager = new XdgForeignManager(host.Display, host.Compositor);
        var parents = new List<(Surface Child, Surface Parent)>();
        manager.ParentRequested += (child, parent) => parents.Add((child, parent));

        var v1Export = MappedToplevel.Map(host, host.Client, color: 0xFF111111);
        var v2Export = MappedToplevel.Map(host, host.Client, color: 0xFF222222);
        var childWindow = MappedToplevel.Map(host, host.Client, color: 0xFF333333);

        Basin.Desktop.Protocol.ZxdgExporterV1? exporterV1 = null;
        Basin.Desktop.Protocol.ZxdgImporterV1? importerV1 = null;
        Basin.Desktop.Protocol.ZxdgExporterV2? exporterV2 = null;
        Basin.Desktop.Protocol.ZxdgImporterV2? importerV2 = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            switch (e.Interface)
            {
                case "zxdg_exporter_v1":
                    exporterV1 = registry.Bind<Basin.Desktop.Protocol.ZxdgExporterV1>(e.Name, 1);
                    break;
                case "zxdg_importer_v1":
                    importerV1 = registry.Bind<Basin.Desktop.Protocol.ZxdgImporterV1>(e.Name, 1);
                    break;
                case "zxdg_exporter_v2":
                    exporterV2 = registry.Bind<Basin.Desktop.Protocol.ZxdgExporterV2>(e.Name, 1);
                    break;
                case "zxdg_importer_v2":
                    importerV2 = registry.Bind<Basin.Desktop.Protocol.ZxdgImporterV2>(e.Name, 1);
                    break;
            }
        };
        host.PumpToClient();
        Assert.NotNull(exporterV1);
        Assert.NotNull(importerV1);
        Assert.NotNull(exporterV2);
        Assert.NotNull(importerV2);

        var exportedV1 = exporterV1!.Export(v1Export.Surface);
        string? handleV1 = null;
        exportedV1.Handle += (_, e) => handleV1 = e.Handle;
        var exportedV2 = exporterV2!.ExportToplevel(v2Export.Surface);
        string? handleV2 = null;
        exportedV2.Handle += (_, e) => handleV2 = e.Handle;
        host.PumpUntil(() => handleV1 is not null && handleV2 is not null);

        var v1ThroughV2 = importerV2!.ImportToplevel(handleV1!);
        v1ThroughV2.SetParentOf(childWindow.Surface);
        var v2ThroughV1 = importerV1!.Import(handleV2!);
        var v2ThroughV1Died = false;
        v2ThroughV1.Destroyed += (_, _) => v2ThroughV1Died = true;
        v2ThroughV1.SetParentOf(childWindow.Surface);
        host.PumpUntil(() => parents.Count == 2);
        Assert.Same(v1Export.ServerSurface, parents[0].Parent);
        Assert.Same(v2Export.ServerSurface, parents[1].Parent);

        v2Export.Toplevel.Dispose();
        v2Export.XdgSurface.Dispose();
        v2Export.Surface.Dispose();
        host.PumpUntil(() => v2ThroughV1Died);

        var stale = importerV1.Import(handleV2!);
        var staleDied = false;
        stale.Destroyed += (_, _) => staleDied = true;
        host.PumpUntil(() => staleDied);

        var bogus = importerV1.Import("no-such-handle");
        var bogusDied = false;
        bogus.Destroyed += (_, _) => bogusDied = true;
        host.PumpUntil(() => bogusDied);

        v1ThroughV2.Dispose();
        v2ThroughV1.Dispose();
        stale.Dispose();
        bogus.Dispose();
        exportedV1.Dispose();
        host.PumpToServer();
    }
}

public sealed class BellDecorationDialogTests
{
    [Fact]
    public void Bell_rings_with_and_without_surface()
    {
        using var host = new CompositorTestHost();
        using var manager = new SystemBellManager(host.Display, host.Compositor, Basin.Capabilities.Defaults.SilentBell.Instance);
        var rings = new List<Surface?>();
        manager.Rang += surface => rings.Add(surface);

        var window = MappedToplevel.Map(host, host.Client);
        Basin.Desktop.Protocol.XdgSystemBellV1? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "xdg_system_bell_v1")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.XdgSystemBellV1>(e.Name, 1);
            }
        };
        host.PumpToClient();

        proxy!.Ring(null);
        proxy.Ring(window.Surface);
        host.PumpUntil(() => rings.Count == 2);
        Assert.Null(rings[0]);
        Assert.Same(window.ServerSurface, rings[1]);
        host.PumpToServer();
    }

    [Fact]
    public void Kde_decoration_negotiates_modes()
    {
        using var host = new CompositorTestHost();
        using var manager = new KdeServerDecorationManager(host.Display, host.Compositor);
        var window = MappedToplevel.Map(host, host.Client);

        Basin.Desktop.Protocol.OrgKdeKwinServerDecorationManager? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "org_kde_kwin_server_decoration_manager")
            {
                proxy = registry.Bind<Basin.Desktop.Protocol.OrgKdeKwinServerDecorationManager>(e.Name, 1);
            }
        };
        var defaultMode = 99u;
        host.PumpToClient();
        Assert.NotNull(proxy);
        proxy!.DefaultMode += (_, e) => defaultMode = e.Mode;

        var requested = new List<KdeServerDecorationManager.DecorationMode>();
        manager.ModeRequested += (_, mode) => requested.Add(mode);

        Assert.Equal(KdeServerDecorationManager.DecorationMode.Client, manager.ModeOf(window.ServerSurface));

        var decoration = proxy.Create(window.Surface);
        var modes = new List<uint>();
        decoration.ModeEvent += (_, e) => modes.Add(e.Mode);
        host.PumpUntil(() => modes.Count == 1);
        Assert.Equal(2u, modes[0]);

        Assert.Equal(KdeServerDecorationManager.DecorationMode.Server, manager.ModeOf(window.ServerSurface));

        decoration.RequestMode(1);
        host.PumpUntil(() => modes.Count == 2);
        Assert.Equal(1u, modes[1]);
        Assert.Equal(KdeServerDecorationManager.DecorationMode.Client, Assert.Single(requested));
        Assert.Equal(KdeServerDecorationManager.DecorationMode.Client, manager.ModeOf(window.ServerSurface));

        decoration.RequestMode(2);
        host.PumpUntil(() => modes.Count == 3);
        Assert.Equal(KdeServerDecorationManager.DecorationMode.Server, manager.ModeOf(window.ServerSurface));

        decoration.Release();
        host.PumpToServer();
        Assert.Equal(KdeServerDecorationManager.DecorationMode.Client, manager.ModeOf(window.ServerSurface));

        host.PumpToServer();
    }

    [Fact]
    public void Dialog_tag_and_icon_events_reach_the_consumer()
    {
        using var host = new CompositorTestHost();
        using var dialogs = new XdgDialogManager(host.Display);
        using var tags = new XdgToplevelTagManager(host.Display);
        using var icons = new XdgToplevelIconManager(host.Display);
        var window = MappedToplevel.Map(host, host.Client);

        var modal = new List<bool>();
        dialogs.ModalChanged += (_, isModal) => modal.Add(isModal);
        var tagValues = new List<string>();
        tags.TagSet += (_, tag) => tagValues.Add(tag);
        var iconNames = new List<string?>();
        icons.IconChanged += (_, name) => iconNames.Add(name);

        Basin.Shell.Xdg.Protocol.XdgWmDialogV1? dialogProxy = null;
        Basin.Shell.Xdg.Protocol.XdgToplevelTagManagerV1? tagProxy = null;
        Basin.Shell.Xdg.Protocol.XdgToplevelIconManagerV1? iconProxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            switch (e.Interface)
            {
                case "xdg_wm_dialog_v1":
                    dialogProxy = registry.Bind<Basin.Shell.Xdg.Protocol.XdgWmDialogV1>(e.Name, 1);
                    break;
                case "xdg_toplevel_tag_manager_v1":
                    tagProxy = registry.Bind<Basin.Shell.Xdg.Protocol.XdgToplevelTagManagerV1>(e.Name, 1);
                    break;
                case "xdg_toplevel_icon_manager_v1":
                    iconProxy = registry.Bind<Basin.Shell.Xdg.Protocol.XdgToplevelIconManagerV1>(e.Name, 1);
                    break;
            }
        };
        host.PumpToClient();
        Assert.NotNull(dialogProxy);
        Assert.NotNull(tagProxy);
        Assert.NotNull(iconProxy);

        var dialog = dialogProxy!.GetXdgDialog(window.Toplevel);
        dialog.SetModal();
        dialog.UnsetModal();
        tagProxy!.SetToplevelTag(window.Toplevel, "main-editor");
        var icon = iconProxy!.CreateIcon();
        icon.SetName("basin-app");
        iconProxy.SetIcon(window.Toplevel, icon);
        host.PumpUntil(() => modal.Count == 2 && tagValues.Count == 1 && iconNames.Count == 1);

        Assert.Equal(new[] { true, false }, modal);
        Assert.Equal("main-editor", tagValues[0]);
        Assert.Equal("basin-app", iconNames[0]);

        dialog.Dispose();
        icon.Dispose();
        host.PumpToServer();
    }
}
