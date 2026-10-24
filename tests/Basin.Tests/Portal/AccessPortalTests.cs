using Basin.Capabilities;
using Basin.Portal;
using Tmds.DBus.Protocol;
using Xunit;

namespace Basin.Tests;

public sealed class AccessPortalTests
{
    private sealed class AcceptedNoPrompts : IPortalPrompts
    {
        private readonly AutoAnswerPrompts _inner = new();

        public ValueTask<PromptOutcome<SourceSelection>> SelectSources(in SourcePrompt prompt, CancellationToken cancellation) => _inner.SelectSources(prompt, cancellation);

        public ValueTask<PromptOutcome<DeviceSelection>> SelectDevices(in DevicePrompt prompt, CancellationToken cancellation) => _inner.SelectDevices(prompt, cancellation);

        public ValueTask<PromptOutcome<Box>> SelectArea(in AreaPrompt prompt, CancellationToken cancellation) => _inner.SelectArea(prompt, cancellation);

        public ValueTask<PromptOutcome<ShortcutBinding[]>> BindShortcuts(in ShortcutPrompt prompt, CancellationToken cancellation) => _inner.BindShortcuts(prompt, cancellation);

        public ValueTask<PromptOutcome<bool>> Confirm(in ConfirmPrompt prompt, CancellationToken cancellation) =>
            ValueTask.FromResult(new PromptOutcome<bool>(PromptResponse.Accepted, false));
    }

    [Fact]
    public void An_accepted_no_from_the_prompt_is_a_refusal()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost(64, 48);
        using var bus = new PortalBus(host.Loop, daemon.Address, PortalBusTests.BusName);
        using var services = new BasinServices(host.Display, host.Loop)
            .Use(host.Layout)
            .Use(bus)
            .Use<IPortalPrompts>(new AcceptedNoPrompts())
            .Install(PortalPack.Default
                .Without("org.freedesktop.impl.portal.Screenshot")
                .Without("org.freedesktop.impl.portal.ScreenCast")
                .Without("org.freedesktop.impl.portal.RemoteDesktop")
                .Without("org.freedesktop.impl.portal.Clipboard")
                .Without("org.freedesktop.impl.portal.InputCapture")
                .Without("org.freedesktop.impl.portal.GlobalShortcuts"))
            .Freeze();
        PortalBusTests.Await(host, bus.Started);
        using var frontend = PortalBusTests.Await(host, PortalTestClient.ConnectAsync(daemon.Address, PortalBusTests.BusName, asFrontend: true));

        var handle = frontend.NextRequest();
        var (response, results) = PortalBusTests.Await(host, frontend.CallRawAsync(
            "org.freedesktop.impl.portal.Access",
            "AccessDialog",
            "osssssa{sv}",
            (ref MessageWriter writer) =>
            {
                writer.WriteObjectPath(handle);
                writer.WriteString("org.example.App");
                writer.WriteString("");
                writer.WriteString("Allow access?");
                writer.WriteString("org.example.App wants access");
                writer.WriteString("");
                writer.WriteDictionary(new Dictionary<string, VariantValue>());
            },
            static (Message m, object? _) => PortalTestClient.ReadResponse(m)));
        Assert.Equal(PortalResponse.Cancelled, response);
        Assert.Empty(results);
    }
}
