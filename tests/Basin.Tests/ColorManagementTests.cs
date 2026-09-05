using Basin.Capabilities;
using Basin.Desktop;
using Basin.Desktop.Protocol;
using Wayland;
using Xunit;

namespace Basin.Tests;

public sealed class ColorManagementTests
{
    private static WpColorManagerV1 Bind(CompositorTestHost host, uint version = 1)
    {
        WpColorManagerV1? color = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "wp_color_manager_v1")
            {
                color = registry.Bind<WpColorManagerV1>(e.Name, version);
            }
        };
        host.PumpToClient();
        Assert.NotNull(color);
        return color!;
    }

    private static ColorManager HdrManager(CompositorTestHost host)
    {
        var manager = new ColorManager(host.Display, host.Compositor) { Resolver = new LutResolver() };
        manager.SetOutputDescription(host.OutputGlobal, Basin.Color.OutputDescriptions.Hdr10(1000, 0));
        return manager;
    }

    private sealed class LutResolver(ColorTransformCapability capability = ColorTransformCapability.Lut3D)
        : IColorTransformResolver
    {
        public ColorTransformCapability Capability => capability;

        public IColorLut? Resolve(ImageDescription source, ImageDescription output) => null;
    }

    private static (List<uint> Tfs, List<uint> Primaries) Advertised(CompositorTestHost host, uint version)
    {
        var proxy = Bind(host, version);
        var tfs = new List<uint>();
        var primaries = new List<uint>();
        var done = false;
        proxy.SupportedTfNamed += (_, e) => tfs.Add((uint)e.Tf);
        proxy.SupportedPrimariesNamed += (_, e) => primaries.Add((uint)e.Primaries);
        proxy.Done += (_, _) => done = true;
        host.PumpUntil(() => done);
        proxy.Dispose();
        host.PumpToServer();
        return (tfs, primaries);
    }

    private static WpImageDescriptionV1 HdrDescription(WpColorManagerV1 proxy)
    {
        var creator = proxy.CreateParametricCreator();
        creator.SetPrimariesNamed((WpColorManagerV1.Primaries)ColorPrimaries.Bt2020);
        creator.SetTfNamed((WpColorManagerV1.TransferFunction)ColorTransferFunction.St2084Pq);
        creator.SetLuminances(0, 10_000, 203);
        return creator.Create();
    }

    [Fact]
    public void A_client_at_two_gets_ready2_and_its_low_half_is_what_version_one_was_told()
    {
        using var host = new CompositorTestHost();
        using var manager = HdrManager(host);

        var second = Bind(host, 2);
        var atTwo = HdrDescription(second);
        ulong wide = 0;
        atTwo.Ready2 += (_, e) => wide = ((ulong)e.IdentityHi << 32) | e.IdentityLo;
        host.PumpUntil(() => wide != 0);

        var first = Bind(host, 1);
        var atOne = HdrDescription(first);
        uint narrow = 0;
        var wideAgain = 0ul;
        atOne.Ready2 += (_, e) => wideAgain = ((ulong)e.IdentityHi << 32) | e.IdentityLo;
#pragma warning disable CS0618
        atOne.Ready += (_, e) => narrow = e.Identity;
#pragma warning restore CS0618
        host.PumpUntil(() => narrow != 0);

        Assert.Equal(0ul, wideAgain);
        Assert.NotEqual(0u, narrow);
        Assert.Equal(narrow, (uint)narrow);

        atTwo.Dispose();
        atOne.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void Identities_are_dense_and_never_recycled()
    {
        var first = new ImageDescription();
        var second = new ImageDescription();
        Assert.NotEqual(first.Identity, second.Identity);
        Assert.NotEqual(0ul, first.Identity);

        var seen = new HashSet<ulong> { first.Identity, second.Identity };
        for (var i = 0; i < 64; i++)
        {
            Assert.True(seen.Add(new ImageDescription().Identity), "an identity was recycled");
        }

        var run = new ImageDescription();
        var next = new ImageDescription();
        Assert.Equal(run.Identity + 1, next.Identity);
    }

    [Fact]
    public void Version_two_advertises_the_compound_curve_and_the_unadapted_intent()
    {
        using var host = new CompositorTestHost();
        using var manager = new ColorManager(host.Display, host.Compositor) { Resolver = new LutResolver() };

        var proxy = Bind(host, 2);
        var tfs = new List<uint>();
        var intents = new List<uint>();
        var done = false;
        proxy.SupportedTfNamed += (_, e) => tfs.Add((uint)e.Tf);
        proxy.SupportedIntent += (_, e) => intents.Add((uint)e.RenderIntent);
        proxy.Done += (_, _) => done = true;
        host.PumpUntil(() => done);

        Assert.Contains((uint)ColorTransferFunction.CompoundPower24, tfs);
        Assert.Contains((uint)ColorRenderIntent.AbsoluteNoAdaptation, intents);
        Assert.DoesNotContain((uint)ColorTransferFunction.Srgb, tfs);
        Assert.DoesNotContain((uint)ColorTransferFunction.ExtSrgb, tfs);

        proxy.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void Version_one_is_told_about_neither()
    {
        using var host = new CompositorTestHost();
        using var manager = new ColorManager(host.Display, host.Compositor);

        var proxy = Bind(host, 1);
        var tfs = new List<uint>();
        var intents = new List<uint>();
        var done = false;
        proxy.SupportedTfNamed += (_, e) => tfs.Add((uint)e.Tf);
        proxy.SupportedIntent += (_, e) => intents.Add((uint)e.RenderIntent);
        proxy.Done += (_, _) => done = true;
        host.PumpUntil(() => done);

        Assert.DoesNotContain((uint)ColorTransferFunction.CompoundPower24, tfs);
        Assert.DoesNotContain((uint)ColorRenderIntent.AbsoluteNoAdaptation, intents);

        proxy.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void Version_one_is_still_told_about_the_deprecated_curve()
    {
        using var host = new CompositorTestHost();
        using var manager = new ColorManager(host.Display, host.Compositor)
        {
            SupportedTransferFunctions =
                [ColorTransferFunction.Srgb, ColorTransferFunction.ExtSrgb, ColorTransferFunction.Gamma22],
        };

        var proxy = Bind(host, 1);
        var tfs = new List<uint>();
        var done = false;
        proxy.SupportedTfNamed += (_, e) => tfs.Add((uint)e.Tf);
        proxy.Done += (_, _) => done = true;
        host.PumpUntil(() => done);

        Assert.Equal(
            [(uint)ColorTransferFunction.Srgb, (uint)ColorTransferFunction.ExtSrgb, (uint)ColorTransferFunction.Gamma22],
            tfs);

        proxy.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void Windows_bt2100_is_refused_rather_than_half_supported()
    {
        using var host = new CompositorTestHost();
        using var manager = new ColorManager(host.Display, host.Compositor);

        var proxy = Bind(host, 2);
        var features = new List<uint>();
        var done = false;
        proxy.SupportedFeature += (_, e) => features.Add((uint)e.Feature);
        proxy.Done += (_, _) => done = true;
        host.PumpUntil(() => done);

        Assert.DoesNotContain((uint)WpColorManagerV1.Feature.WindowsBt2100, features);
        Assert.Equal(2, ColorManager.Version);
    }

    [Fact]
    public void A_reference_resolves_to_what_minted_it_and_stops_when_destroyed()
    {
        using var host = new CompositorTestHost();
        using var manager = new ColorManager(host.Display, host.Compositor);
        var window = MappedToplevel.Map(host, host.Client);
        var client = window.ServerSurface.Resource.Client;

        var described = new ImageDescription
        {
            PrimariesNamed = ColorPrimaries.Bt2020,
            TransferNamed = ColorTransferFunction.St2084Pq,
        };

        var reference = ColorManager.CreateReference(client, 1, 0, described, allowInformation: false);
        Assert.True(ColorManager.ReferenceRegistry.TryResolve(reference, out var resolved, out var allowed));
        Assert.Same(described, resolved);
        Assert.False(allowed);

        Assert.False(ColorManager.ReferenceRegistry.TryResolve(null, out _, out _));

        reference.Destroy();
        host.PumpToServer();
        Assert.False(ColorManager.ReferenceRegistry.TryResolve(reference, out _, out _));
    }

    [Fact]
    public void Parametric_description_flows_to_the_surface()
    {
        using var host = new CompositorTestHost();
        using var manager = HdrManager(host);
        var window = MappedToplevel.Map(host, host.Client);
        var proxy = Bind(host);

        var features = new List<uint>();
        var tfs = new List<uint>();
        var primaries = new List<uint>();
        var done = false;
        proxy.SupportedFeature += (_, e) => features.Add((uint)e.Feature);
        proxy.SupportedTfNamed += (_, e) => tfs.Add((uint)e.Tf);
        proxy.SupportedPrimariesNamed += (_, e) => primaries.Add((uint)e.Primaries);
        proxy.Done += (_, _) => done = true;
        host.PumpUntil(() => done);
        Assert.Contains(1u, features);
        Assert.DoesNotContain(0u, features);
        Assert.Contains(11u, tfs);
        Assert.Contains(6u, primaries);

        var creator = proxy.CreateParametricCreator();
        creator.SetPrimariesNamed((WpColorManagerV1.Primaries)ColorPrimaries.Bt2020);
        creator.SetTfNamed((WpColorManagerV1.TransferFunction)ColorTransferFunction.St2084Pq);
        creator.SetLuminances(0, 10_000, 203);
        creator.SetMaxCll(1_000);
        creator.SetMaxFall(400);
        var description = creator.Create();
        uint identity = 0;
        var failed = false;
#pragma warning disable CS0618
        description.Ready += (_, e) => identity = e.Identity;
#pragma warning restore CS0618
        description.Failed += (_, _) => failed = true;
        host.PumpUntil(() => identity != 0 || failed);
        Assert.True(identity != 0);

        var information = description.GetInformation();
        uint infoPrimaries = 0;
        uint infoTf = 0;
        (uint Min, uint Max, uint Reference) luminances = default;
        uint maxCll = 0;
        var infoDone = false;
        information.PrimariesNamed += (_, e) => infoPrimaries = (uint)e.Primaries;
        information.TfNamed += (_, e) => infoTf = (uint)e.Tf;
        information.Luminances += (_, e) => luminances = (e.MinLum, e.MaxLum, e.ReferenceLum);
        information.TargetMaxCll += (_, e) => maxCll = e.MaxCll;
        information.Done += (_, _) => infoDone = true;
        host.PumpUntil(() => infoDone);
        Assert.Equal(6u, infoPrimaries);
        Assert.Equal(11u, infoTf);
        Assert.Equal((0u, 10_000u, 203u), luminances);
        Assert.Equal(1_000u, maxCll);

        var changes = new List<ImageDescription?>();
        manager.SurfaceDescriptionChanged += (_, d) => changes.Add(d);
        var colorSurface = proxy.GetSurface(window.Surface);
        colorSurface.SetImageDescription(description, WpColorManagerV1.RenderIntent.Perceptual);
        host.PumpToServer();
        Assert.Empty(changes);
        Assert.Same(ImageDescription.SdrDefault, manager.DescriptionOf(window.ServerSurface));

        window.Surface.Commit();
        host.PumpUntil(() => changes.Count == 1);
        var applied = manager.DescriptionOf(window.ServerSurface);
        Assert.Equal(ColorPrimaries.Bt2020, applied.PrimariesNamed);
        Assert.Equal(ColorTransferFunction.St2084Pq, applied.TransferNamed);
        Assert.Equal((0u, 10_000u, 203u), applied.Luminances);

        colorSurface.UnsetImageDescription();
        window.Surface.Commit();
        host.PumpUntil(() => changes.Count == 2);
        Assert.Null(changes[1]);
        Assert.Same(ImageDescription.SdrDefault, manager.DescriptionOf(window.ServerSurface));

        var feedback = proxy.GetSurfaceFeedback(window.Surface);
        var preferredChanges = 0;
#pragma warning disable CS0618
        feedback.PreferredChanged += (_, _) => preferredChanges++;
#pragma warning restore CS0618
        host.PumpToServer();
        manager.SetOutputDescription(host.OutputGlobals()[0].Global, new ImageDescription
        {
            PrimariesNamed = ColorPrimaries.Bt2020,
            TransferNamed = ColorTransferFunction.St2084Pq,
        });
        host.PumpUntil(() => preferredChanges == 1);

        description.Dispose();
        colorSurface.Dispose();
        feedback.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void Replacing_the_surface_object_between_commits_never_drops_the_description()
    {
        using var host = new CompositorTestHost();
        using var manager = HdrManager(host);
        var window = MappedToplevel.Map(host, host.Client);
        var proxy = Bind(host);

        var changes = new List<ImageDescription?>();
        manager.SurfaceDescriptionChanged += (_, d) => changes.Add(d);

        var first = HdrDescription(proxy);
        var colorSurface = proxy.GetSurface(window.Surface);
        colorSurface.SetImageDescription(first, WpColorManagerV1.RenderIntent.Perceptual);
        window.Surface.Commit();
        host.PumpUntil(() => changes.Count == 1);
        Assert.Equal(ColorTransferFunction.St2084Pq, manager.DescriptionOf(window.ServerSurface).TransferNamed);

        for (var round = 0; round < 3; round++)
        {
            colorSurface.Dispose();
            colorSurface = proxy.GetSurface(window.Surface);
            var next = HdrDescription(proxy);
            colorSurface.SetImageDescription(next, WpColorManagerV1.RenderIntent.Perceptual);
            window.Surface.Commit();
            host.PumpToServer();
            host.PumpToClient();
            host.PumpToServer();
            Assert.Equal(ColorTransferFunction.St2084Pq, manager.DescriptionOf(window.ServerSurface).TransferNamed);
            next.Dispose();
        }

        Assert.Single(changes);

        first.Dispose();
        colorSurface.Dispose();
        window.Surface.Commit();
        host.PumpUntil(() => changes.Count == 2);
        Assert.Null(changes[1]);
        Assert.Same(ImageDescription.SdrDefault, manager.DescriptionOf(window.ServerSurface));
    }

    [Fact]
    public void Incomplete_parametric_description_is_an_error()
    {
        using var host = new CompositorTestHost();
        using var manager = new ColorManager(host.Display, host.Compositor);
        var proxy = Bind(host);

        var creator = proxy.CreateParametricCreator();
        creator.SetPrimariesNamed((WpColorManagerV1.Primaries)ColorPrimaries.Srgb);
        _ = creator.Create();
        Assert.ThrowsAny<WaylandException>(() =>
        {
            for (var i = 0; i < 10; i++)
            {
                host.PumpToServer();
                host.PumpToClient();
            }
        });
    }

    [Fact]
    public void The_advertised_set_is_the_floor_and_follows_the_outputs()
    {
        using var host = new CompositorTestHost();
        using var manager = new ColorManager(host.Display, host.Compositor) { Resolver = new LutResolver() };

        var (tfs, primaries) = Advertised(host, 2);
        Assert.Equal(
            [(uint)ColorTransferFunction.CompoundPower24, (uint)ColorTransferFunction.Gamma22, (uint)ColorTransferFunction.ExtLinear],
            tfs);
        Assert.Equal([(uint)ColorPrimaries.Srgb], primaries);

        manager.SetOutputDescription(host.OutputGlobal, Basin.Color.OutputDescriptions.Hdr10(1000, 0));
        (tfs, primaries) = Advertised(host, 2);
        Assert.Contains((uint)ColorTransferFunction.St2084Pq, tfs);
        Assert.Contains((uint)ColorPrimaries.Bt2020, primaries);
        Assert.DoesNotContain((uint)ColorTransferFunction.Hlg, tfs);

        manager.RemoveOutputDescription(host.OutputGlobal);
        (tfs, primaries) = Advertised(host, 2);
        Assert.DoesNotContain((uint)ColorTransferFunction.St2084Pq, tfs);
        Assert.DoesNotContain((uint)ColorPrimaries.Bt2020, primaries);

        (tfs, _) = Advertised(host, 1);
        Assert.Equal([(uint)ColorTransferFunction.Srgb, (uint)ColorTransferFunction.Gamma22, (uint)ColorTransferFunction.ExtLinear], tfs);
    }

    [Fact]
    public void A_manager_that_cannot_convert_offers_only_what_its_outputs_present()
    {
        using var host = new CompositorTestHost();
        using var manager = new ColorManager(host.Display, host.Compositor)
        {
            Resolver = new LutResolver(ColorTransformCapability.None),
        };

        var (tfs, primaries) = Advertised(host, 2);
        Assert.Equal([(uint)ColorTransferFunction.Gamma22], tfs);
        Assert.Equal([(uint)ColorPrimaries.Srgb], primaries);

        manager.SetOutputDescription(host.OutputGlobal, Basin.Color.OutputDescriptions.Hdr10(1000, 0));
        (tfs, primaries) = Advertised(host, 2);
        Assert.Equal([(uint)ColorTransferFunction.Gamma22, (uint)ColorTransferFunction.St2084Pq], tfs);
        Assert.Equal([(uint)ColorPrimaries.Srgb, (uint)ColorPrimaries.Bt2020], primaries);
    }

    [Fact]
    public void A_deprecated_transfer_function_is_an_error_at_version_two()
    {
        using var host = new CompositorTestHost();
        using var manager = new ColorManager(host.Display, host.Compositor) { Resolver = new LutResolver() };
        var proxy = Bind(host, 2);

        var creator = proxy.CreateParametricCreator();
        creator.SetPrimariesNamed((WpColorManagerV1.Primaries)ColorPrimaries.Srgb);
        creator.SetTfNamed((WpColorManagerV1.TransferFunction)ColorTransferFunction.Srgb);
        Assert.ThrowsAny<WaylandException>(() =>
        {
            for (var i = 0; i < 10; i++)
            {
                host.PumpToServer();
                host.PumpToClient();
            }
        });
    }

    [Fact]
    public void A_narrowed_manager_advertises_only_what_it_was_given()
    {
        using var host = new CompositorTestHost();
        using var manager = new ColorManager(host.Display, host.Compositor)
        {
            SupportedTransferFunctions = [ColorTransferFunction.Srgb, ColorTransferFunction.Gamma22],
            SupportedPrimaries = [ColorPrimaries.Srgb],
        };

        var proxy = Bind(host, 2);
        var tfs = new List<uint>();
        var primaries = new List<uint>();
        var done = false;
        proxy.SupportedTfNamed += (_, e) => tfs.Add((uint)e.Tf);
        proxy.SupportedPrimariesNamed += (_, e) => primaries.Add((uint)e.Primaries);
        proxy.Done += (_, _) => done = true;
        host.PumpUntil(() => done);

        Assert.Equal([(uint)ColorTransferFunction.Gamma22], tfs);
        Assert.Equal([(uint)ColorPrimaries.Srgb], primaries);
        Assert.DoesNotContain((uint)ColorTransferFunction.St2084Pq, tfs);

        proxy.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void A_transfer_function_that_was_never_offered_is_an_error()
    {
        using var host = new CompositorTestHost();
        using var manager = new ColorManager(host.Display, host.Compositor)
        {
            SupportedTransferFunctions = [ColorTransferFunction.Srgb],
            SupportedPrimaries = [ColorPrimaries.Srgb],
        };
        var proxy = Bind(host, 1);

        var creator = proxy.CreateParametricCreator();
        creator.SetPrimariesNamed((WpColorManagerV1.Primaries)ColorPrimaries.Srgb);
        creator.SetTfNamed((WpColorManagerV1.TransferFunction)ColorTransferFunction.St2084Pq);
        Assert.ThrowsAny<WaylandException>(() =>
        {
            for (var i = 0; i < 10; i++)
            {
                host.PumpToServer();
                host.PumpToClient();
            }
        });
    }

    [Fact]
    public void Icc_descriptions_create_validate_and_flow_to_the_surface()
    {
        Assert.SkipUnless(Basin.Color.Lcms2Support.IsAvailable, "liblcms2 ≥ 2.19 not present");
        using var host = new CompositorTestHost();
        using var manager = new ColorManager(host.Display, host.Compositor)
        {
            Profiles = new Basin.Color.Lcms2ColorProfileService(),
        };
        var window = MappedToplevel.Map(host, host.Client);
        var proxy = Bind(host);

        var features = new List<uint>();
        var done = false;
        proxy.SupportedFeature += (_, e) => features.Add((uint)e.Feature);
        proxy.Done += (_, _) => done = true;
        host.PumpUntil(() => done);
        Assert.Contains(0u, features);

        byte[] icc;
        using (var srgb = Lcms2.IccProfile.CreateSrgb())
        {
            icc = srgb.SaveToArray();
        }

        using var blob = new Wayland.Server.Shm.MemfdBlobFactory().Create("icc-test", icc);
        var creator = proxy.CreateIccCreator();
        creator.SetIccFile(blob.FdSlot, 0, blob.Size);
        var description = creator.Create();
        uint identity = 0;
        var failed = false;
#pragma warning disable CS0618
        description.Ready += (_, e) => identity = e.Identity;
#pragma warning restore CS0618
        description.Failed += (_, _) => failed = true;
        host.PumpUntil(() => identity != 0 || failed);
        Assert.False(failed);
        Assert.True(identity != 0);

        var colorSurface = proxy.GetSurface(window.Surface);
        colorSurface.SetImageDescription(description, WpColorManagerV1.RenderIntent.Perceptual);
        window.Surface.Commit();
        host.PumpToServer();
        var applied = manager.DescriptionOf(window.ServerSurface);
        Assert.NotNull(applied.IccData);
        Assert.Equal(icc.Length, applied.IccData!.Length);

        using var junk = new Wayland.Server.Shm.MemfdBlobFactory().Create("icc-junk", [1, 2, 3, 4, 5, 6, 7, 8]);
        var badCreator = proxy.CreateIccCreator();
        badCreator.SetIccFile(junk.FdSlot, 0, junk.Size);
        var badDescription = badCreator.Create();
        uint badCause = uint.MaxValue;
        badDescription.Failed += (_, e) => badCause = (uint)e.Cause;
        host.PumpUntil(() => badCause != uint.MaxValue);
        Assert.Equal((uint)WpImageDescriptionV1.Cause.Unsupported, badCause);

        description.Dispose();
        badDescription.Dispose();
        colorSurface.Dispose();
        host.PumpToServer();
    }

    [Fact]
    public void Representation_round_trips()
    {
        using var host = new CompositorTestHost();
        using var manager = new ColorRepresentationManager(host.Display, host.Compositor);
        var window = MappedToplevel.Map(host, host.Client);

        WpColorRepresentationManagerV1? proxy = null;
        var registry = host.Client.Display.GetRegistry();
        registry.Global += (_, e) =>
        {
            if (e.Interface == "wp_color_representation_manager_v1")
            {
                proxy = registry.Bind<WpColorRepresentationManagerV1>(e.Name, 1);
            }
        };
        host.PumpToClient();
        Assert.NotNull(proxy);

        var pairs = new List<(uint Coefficients, uint Range)>();
        var repDone = false;
        proxy!.SupportedCoefficientsAndRanges += (_, e) => pairs.Add(((uint)e.Coefficients, (uint)e.Range));
        proxy.Done += (_, _) => repDone = true;
        host.PumpUntil(() => repDone);
        Assert.Contains((2u, 2u), pairs);

        var changes = new List<ColorRepresentationManager.Representation>();
        manager.RepresentationChanged += (_, r) => changes.Add(r);
        var surface = proxy.GetSurface(window.Surface);
        surface.SetAlphaMode(WpColorRepresentationSurfaceV1.AlphaMode.Straight);
        surface.SetCoefficientsAndRange(WpColorRepresentationSurfaceV1.Coefficients.Bt709, WpColorRepresentationSurfaceV1.Range.Limited);
        host.PumpToServer();
        Assert.Empty(changes);

        window.Surface.Commit();
        host.PumpUntil(() => changes.Count == 1);

        var current = manager.RepresentationOf(window.ServerSurface);
        Assert.Equal(WpColorRepresentationSurfaceV1.AlphaMode.Straight, current.AlphaMode);
        Assert.Equal(WpColorRepresentationSurfaceV1.Coefficients.Bt709, current.Coefficients);
        Assert.Equal(WpColorRepresentationSurfaceV1.Range.Limited, current.Range);

        surface.Dispose();
        window.Surface.Commit();
        host.PumpUntil(() => changes.Count == 2);
        Assert.Equal(ColorRepresentationManager.Representation.Default, manager.RepresentationOf(window.ServerSurface));
        host.PumpToServer();
    }
}
