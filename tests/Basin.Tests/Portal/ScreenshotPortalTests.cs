using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Portal;
using Basin.Scene;
using Basin.Tests.PortalClient;
using Tmds.DBus.Protocol;
using Xunit;

namespace Basin.Tests;

public sealed class ScreenshotPortalTests
{
    private const string Impl = "org.freedesktop.impl.portal.Screenshot";

    private static string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "basin-shots-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static BasinServices Services(CompositorTestHost host, PortalBus bus, IScreenCapture capture, IPortalPrompts prompts, string shots, Action<BasinServices>? more = null)
    {
        var services = new BasinServices(host.Display, host.Loop)
            .Use(host.Layout)
            .Use(bus)
            .Use(capture)
            .Use(prompts)
            .Use(new PortalOptions { ScreenshotDirectory = shots });
        more?.Invoke(services);
        return services.Install(PortalPack.Default.Without("org.freedesktop.impl.portal.ScreenCast").Without("org.freedesktop.impl.portal.Clipboard").Without("org.freedesktop.impl.portal.InputCapture").Without("org.freedesktop.impl.portal.GlobalShortcuts")).Freeze();
    }

    private static void Ready(CompositorTestHost host, PortalBus bus) => PortalBusTests.Await(host, bus.Started);

    [Fact]
    public void An_interactive_area_screenshot_goes_through_the_distro_frontend()
    {
        PortalFrontendFixture.SkipUnlessAvailable();
        using var host = new CompositorTestHost(64, 48);
        using var fixture = PortalFrontendFixture.Start(Impl);
        _ = new SceneRect(host.Scene.Root, 64, 48, new RenderColor(0f, 0f, 1f, 1f));
        var capture = new SceneScreenCapture(host.Scene, host.Layout) { Renderer = host.Renderer, Background = RenderColor.Black };
        var prompts = new AutoAnswerPrompts { Area = new Box(10, 10, 20, 15) };
        var shots = TempDirectory();
        using var bus = new PortalBus(host.Loop, fixture.Address, PortalBusTests.BusName);
        using var services = Services(host, bus, capture, prompts, shots);
        Ready(host, bus);
        fixture.StartFrontend(host, bus);

        using var client = PortalBusTests.Await(host, PortalTestClient.ConnectAsync(fixture.Address, PortalBus.FrontendName, asFrontend: false));
        var screenshot = new Screenshot(client.Connection, PortalBus.FrontendName, new ObjectPath(PortalBus.RootPath));
        Assert.Equal(3u, PortalBusTests.Await(host, screenshot.GetVersionAsync()));
        Assert.Equal(PortalScreenshotModule.TargetScreen | PortalScreenshotModule.TargetArea, PortalBusTests.Await(host, screenshot.GetAvailableTargetsAsync()));

        var requestPath = PortalClientSupport.RequestPath(client.Connection, "shot1");
        var request = new Request(client.Connection, PortalBus.FrontendName, requestPath);
        (uint Response, Dictionary<string, VariantValue> Results)? reply = null;
        using var watch = PortalBusTests.Await(host, request.WatchResponseAsync(r => reply = r, emitOnCapturedContext: false).AsTask());
        var handle = PortalBusTests.Await(host, screenshot.ScreenshotAsync("", new Dictionary<string, VariantValue>
        {
            ["handle_token"] = VariantValue.String("shot1"),
            ["interactive"] = VariantValue.Bool(true),
        }));
        Assert.Equal(requestPath, handle);
        PortalBusTests.PumpUntil(host, () => reply is not null, rounds: 1500);
        Assert.True(reply!.Value.Response == 0, "response " + reply.Value.Response + "\n" + fixture.Dump());
        var uri = new Uri(reply.Value.Results["uri"].GetString());
        Assert.True(File.Exists(uri.LocalPath), uri.LocalPath);
        Assert.StartsWith(shots, uri.LocalPath, StringComparison.Ordinal);

        var (rgba, width, height) = PngCodec.Decode(File.ReadAllBytes(uri.LocalPath));
        Assert.Equal((20, 15), (width, height));
        Assert.Equal((0, 0, 255), (rgba[0], rgba[1], rgba[2]));
        var area = Assert.IsType<AreaPrompt>(Assert.Single(prompts.Asked));
        Assert.False(area.PickPoint);
        Assert.Null(area.Output);
        Directory.Delete(shots, recursive: true);
    }

    [Fact]
    public void A_non_interactive_screenshot_the_frontend_checked_asks_nothing()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost(64, 48);
        _ = new SceneRect(host.Scene.Root, 64, 48, new RenderColor(1f, 0f, 0f, 1f));
        var capture = new SceneScreenCapture(host.Scene, host.Layout) { Renderer = host.Renderer, Background = RenderColor.Black };
        var prompts = new AutoAnswerPrompts();
        var shots = TempDirectory();
        using var bus = new PortalBus(host.Loop, daemon.Address, PortalBusTests.BusName);
        using var services = Services(host, bus, capture, prompts, shots);
        Ready(host, bus);
        using var frontend = PortalBusTests.Await(host, PortalTestClient.ConnectAsync(daemon.Address, PortalBusTests.BusName, asFrontend: true));

        var (response, results) = PortalBusTests.Await(host, frontend.CallScreenshotAsync(
            "org.example.App", "", new Dictionary<string, VariantValue> { ["permission_store_checked"] = VariantValue.Bool(true) }));
        Assert.Equal(0u, response);
        Assert.Empty(prompts.Asked);
        var (rgba, width, height) = PngCodec.Decode(File.ReadAllBytes(new Uri(results["uri"].GetString()).LocalPath));
        Assert.Equal((64, 48), (width, height));
        Assert.Equal((255, 0, 0), (rgba[0], rgba[1], rgba[2]));

        (response, _) = PortalBusTests.Await(host, frontend.CallScreenshotAsync("org.example.App", "", []));
        Assert.Equal(0u, response);
        var confirm = Assert.IsType<ConfirmPrompt>(Assert.Single(prompts.Asked));
        Assert.Equal("org.example.App", confirm.AppId);

        prompts.ConfirmAnswer = false;
        (response, results) = PortalBusTests.Await(host, frontend.CallScreenshotAsync("org.example.App", "", []));
        Assert.Equal(1u, response);
        Assert.Empty(results);
        Directory.Delete(shots, recursive: true);
    }

    [Fact]
    public void Window_targets_capture_the_chosen_or_active_toplevel()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost(64, 48);
        var capture = new ToplevelCapture(host);
        var model = new TestToplevelModel();
        var stack = new TestToplevelStack();
        var first = model.Add("Editor", "org.example.Editor");
        var second = model.Add("Terminal", "org.example.Terminal");
        stack.SetOrder(first, second);
        var prompts = new AutoAnswerPrompts { Sources = AutoAnswerPrompts.SourceAnswer.Toplevel, ToplevelId = first };
        var shots = TempDirectory();
        using var bus = new PortalBus(host.Loop, daemon.Address, PortalBusTests.BusName);
        using var services = Services(host, bus, capture, prompts, shots, s => s.Use<IToplevelModel>(model).Use<IToplevelStack>(stack));
        Ready(host, bus);
        using var frontend = PortalBusTests.Await(host, PortalTestClient.ConnectAsync(daemon.Address, PortalBusTests.BusName, asFrontend: true));
        Assert.Equal(15u, PortalBusTests.Await(host, frontend.GetPropertyAsync(PortalBus.RootPath, Impl, "AvailableTargets")).GetUInt32());

        var (response, _) = PortalBusTests.Await(host, frontend.CallScreenshotAsync("org.example.App", "", new Dictionary<string, VariantValue>
        {
            ["interactive"] = VariantValue.Bool(true),
            ["target"] = VariantValue.UInt32(PortalScreenshotModule.TargetWindow),
        }));
        Assert.Equal(0u, response);
        Assert.Equal([first], capture.Captured);
        var sources = Assert.IsType<SourcePrompt>(Assert.Single(prompts.Asked));
        Assert.Equal(PromptSourceKinds.Window, sources.Kinds);
        Assert.Equal(2, sources.Toplevels.Count);

        (response, _) = PortalBusTests.Await(host, frontend.CallScreenshotAsync("org.example.App", "", new Dictionary<string, VariantValue>
        {
            ["permission_store_checked"] = VariantValue.Bool(true),
            ["target"] = VariantValue.UInt32(PortalScreenshotModule.TargetActiveWindow),
        }));
        Assert.Equal(0u, response);
        Assert.Equal([first, second], capture.Captured);

        (response, _) = PortalBusTests.Await(host, frontend.CallScreenshotAsync("org.example.App", "", new Dictionary<string, VariantValue>
        {
            ["target"] = VariantValue.UInt32(16),
        }));
        Assert.Equal(2u, response);
        Directory.Delete(shots, recursive: true);
    }

    [Fact]
    public void PickColor_answers_the_pixel_in_linear_light()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost(64, 48);
        _ = new SceneRect(host.Scene.Root, 64, 48, new RenderColor(0.5f, 0f, 0f, 1f));
        var capture = new SceneScreenCapture(host.Scene, host.Layout) { Renderer = host.Renderer, Background = RenderColor.Black };
        var prompts = new AutoAnswerPrompts { Area = new Box(5, 5, 1, 1) };
        using var bus = new PortalBus(host.Loop, daemon.Address, PortalBusTests.BusName);
        using var services = Services(host, bus, capture, prompts, TempDirectory());
        Ready(host, bus);
        using var frontend = PortalBusTests.Await(host, PortalTestClient.ConnectAsync(daemon.Address, PortalBusTests.BusName, asFrontend: true));

        var (response, results) = PortalBusTests.Await(host, frontend.CallImplAsync(Impl, "PickColor", "org.example.App", "", []));
        Assert.Equal(0u, response);
        var color = results["color"];
        Assert.Equal(VariantValueType.Struct, color.Type);
        Assert.Equal(PortalScreenshotModule.SrgbToLinear(128), color.GetItem(0).GetDouble(), 0.02);
        Assert.Equal(0, color.GetItem(1).GetDouble(), 0.001);
        Assert.Equal(0, color.GetItem(2).GetDouble(), 0.001);
        var area = Assert.IsType<AreaPrompt>(Assert.Single(prompts.Asked));
        Assert.True(area.PickPoint);
    }

    [Fact]
    public void Closing_the_request_during_a_held_prompt_answers_other()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost(64, 48);
        var capture = new SceneScreenCapture(host.Scene, host.Layout) { Renderer = host.Renderer };
        var prompts = new AutoAnswerPrompts { Hold = true };
        using var bus = new PortalBus(host.Loop, daemon.Address, PortalBusTests.BusName);
        using var services = Services(host, bus, capture, prompts, TempDirectory());
        Ready(host, bus);
        using var frontend = PortalBusTests.Await(host, PortalTestClient.ConnectAsync(daemon.Address, PortalBusTests.BusName, asFrontend: true));

        var handle = new ObjectPath(PortalBus.RootPath + "/request/1_1/held");
        var pending = frontend.CallScreenshotAsync("org.example.App", "", new Dictionary<string, VariantValue> { ["interactive"] = VariantValue.Bool(true) }, handle);
        PortalBusTests.PumpUntil(host, () => prompts.HeldCount == 1);
        Assert.False(pending.IsCompleted);

        PortalBusTests.Await(host, frontend.CallAsync(handle.ToString(), "org.freedesktop.impl.portal.Request", "Close"));
        var (response, results) = PortalBusTests.Await(host, pending);
        Assert.Equal(2u, response);
        Assert.Empty(results);
        Assert.Equal(0, prompts.HeldCount);
        Assert.Empty(bus.Sessions);
    }

    [Fact]
    public void The_module_refuses_to_freeze_without_prompts_and_declines_without_a_bus()
    {
        using var host = new CompositorTestHost();
        using var bus = new PortalBus(host.Loop, "unix:path=/nonexistent", PortalBusTests.BusName);
        using var services = new BasinServices(host.Display, host.Loop)
            .Use(host.Layout)
            .Use(bus)
            .Use<IScreenCapture>(new TestScreenCapture(host))
            .Install(PortalPack.Default
                .Without("org.freedesktop.impl.portal.ScreenCast")
                .Without("org.freedesktop.impl.portal.Clipboard")
                .Without("org.freedesktop.impl.portal.RemoteDesktop")
                .Without("org.freedesktop.impl.portal.InputCapture")
                .Without("org.freedesktop.impl.portal.GlobalShortcuts")
                .Without("org.freedesktop.impl.portal.Access"));
        var error = Assert.Throws<InvalidOperationException>(() => services.Freeze());
        Assert.Contains(Impl, error.Message);
        Assert.Contains(nameof(IPortalPrompts), error.Message);
        Assert.Contains($"Without(\"{Impl}\")", error.Message);
    }
}
