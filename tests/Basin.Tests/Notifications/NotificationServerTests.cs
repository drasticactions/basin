using Basin.Notifications;
using Tmds.DBus.Protocol;
using Xunit;
using Notification = Basin.Notifications.Notification;

namespace Basin.Tests;

public sealed class NotificationServerTests
{
    private static NotificationServer Start(CompositorTestHost host, PrivateBus daemon, NotificationServerOptions? options = null)
    {
        var server = NotificationServer.TryCreate(host.Loop, (options ?? new NotificationServerOptions()) with { Address = daemon.Address }, out var reason);
        Assert.True(server is not null, reason);
        Assert.True(server.OwnsName);
        return server;
    }

    private static uint Notify(CompositorTestHost host, Task<uint> call) => PortalBusTests.Await(host, call);

    [Fact]
    public void Notify_returns_an_id_with_the_hints_read_and_replaces_id_replaces_it()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost();
        using var server = Start(host, daemon);
        using var client = NotificationTestClient.Connect(host, daemon.Address);
        var added = new List<Notification>();
        var replaced = new List<(Notification Previous, Notification Next)>();
        server.Added += added.Add;
        server.Replaced += (previous, next) => replaced.Add((previous, next));

        var id = Notify(host, client.NotifyAsync("Hello", "first", actions: ["default", "Open", "later", "Later"], expireTimeout: 0, hints: (ref MessageWriter writer) =>
        {
            NotificationTestClient.ByteHint(ref writer, "urgency", 2);
            NotificationTestClient.StringHint(ref writer, "category", "im.received");
            NotificationTestClient.StringHint(ref writer, "desktop-entry", "org.example.Chat");
            NotificationTestClient.StringHint(ref writer, "sound-name", "message-new-instant");
            NotificationTestClient.StringHint(ref writer, "sound-file", "/tmp/ding.oga");
            NotificationTestClient.BoolHint(ref writer, "suppress-sound", true);
            NotificationTestClient.BoolHint(ref writer, "resident", true);
            NotificationTestClient.BoolHint(ref writer, "transient", true);
            NotificationTestClient.StringHint(ref writer, "x-unknown", "ignored");
        }, appIcon: "chat"));

        Assert.NotEqual(0u, id);
        var notification = Assert.Single(added);
        Assert.Equal(id, notification.Id);
        Assert.Equal("notify-test", notification.AppName);
        Assert.Equal("chat", notification.AppIcon);
        Assert.Equal("Hello", notification.Summary);
        Assert.Equal("first", notification.Body);
        Assert.Equal([new NotificationAction("default", "Open"), new NotificationAction("later", "Later")], notification.Actions);
        Assert.Equal(NotificationUrgency.Critical, notification.Urgency);
        Assert.Equal("im.received", notification.Category);
        Assert.Equal("org.example.Chat", notification.DesktopEntry);
        Assert.Equal("message-new-instant", notification.SoundName);
        Assert.Equal("/tmp/ding.oga", notification.SoundFile);
        Assert.True(notification.SuppressSound);
        Assert.True(notification.Resident);
        Assert.True(notification.Transient);
        Assert.Equal(0, notification.ExpireTimeout);
        Assert.Null(notification.ExpiresAfter);
        Assert.Equal(client.UniqueName, notification.Sender);

        var again = Notify(host, client.NotifyAsync("Hello", "second", replacesId: id, expireTimeout: 0));
        Assert.Equal(id, again);
        var (previous, next) = Assert.Single(replaced);
        Assert.Equal("first", previous.Body);
        Assert.Equal("second", next.Body);
        Assert.Equal(id, next.Id);
        Assert.Same(next, Assert.Single(server.Active));
        Assert.Single(added);

        var fresh = Notify(host, client.NotifyAsync("Other", replacesId: 9999, expireTimeout: 0));
        Assert.NotEqual(id, fresh);
        Assert.NotEqual(9999u, fresh);
        Assert.Equal(2, server.Active.Count);
    }

    [Fact]
    public void The_server_reports_its_capabilities_and_information()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost();
        using var server = Start(host, daemon, new NotificationServerOptions { ServerName = "luna-wm", Vendor = "basin", Version = "3.0.5" });
        using var client = NotificationTestClient.Connect(host, daemon.Address);

        var capabilities = PortalBusTests.Await(host, client.CapabilitiesAsync());
        Assert.Equal(["actions", "body", "body-markup", "icon-static", "persistence", "sound"], capabilities);

        var information = PortalBusTests.Await(host, client.ServerInformationAsync());
        Assert.Equal(("luna-wm", "basin", "3.0.5", "1.2"), information);
    }

    [Fact]
    public void CloseNotification_emits_reason_3_and_Dismiss_emits_reason_2()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost();
        using var server = Start(host, daemon);
        using var client = NotificationTestClient.Connect(host, daemon.Address);
        var closed = new List<(uint Id, NotificationCloseReason Reason)>();
        server.Closed += (notification, reason) => closed.Add((notification.Id, reason));

        var first = Notify(host, client.NotifyAsync("one", expireTimeout: 0));
        var second = Notify(host, client.NotifyAsync("two", expireTimeout: 0));

        PortalBusTests.Await(host, client.CloseAsync(first));
        PortalBusTests.PumpUntil(host, () => client.Signals().Count == 1);
        Assert.Equal(("NotificationClosed", first, "3"), client.Signals()[0]);

        Assert.True(server.Dismiss(second));
        Assert.False(server.Dismiss(second));
        PortalBusTests.PumpUntil(host, () => client.Signals().Count == 2);
        Assert.Equal(("NotificationClosed", second, "2"), client.Signals()[1]);

        Assert.Equal([(first, NotificationCloseReason.Closed), (second, NotificationCloseReason.Dismissed)], closed);
        Assert.Empty(server.Active);

        PortalBusTests.Await(host, client.CloseAsync(first));
    }

    [Fact]
    public void A_critical_notification_never_expires()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost();
        using var server = Start(host, daemon, new NotificationServerOptions { DefaultExpireMs = 40 });
        var closed = new List<NotificationCloseReason>();
        server.Closed += (_, reason) => closed.Add(reason);

        var defaulted = server.Post(new Notification { Summary = "Battery low", Urgency = NotificationUrgency.Critical });
        var timed = server.Post(new Notification { Summary = "Battery lower", Urgency = NotificationUrgency.Critical, ExpireTimeout = 40 });
        var deadline = Environment.TickCount64 + 200;
        while (Environment.TickCount64 < deadline)
        {
            host.Loop.Dispatch(10);
        }

        Assert.Empty(closed);
        Assert.Null(server.Find(defaulted)!.ExpiresAfter);
        Assert.Null(server.Find(timed)!.ExpiresAfter);

        server.Post(new Notification { Summary = "Battery fine", ExpireTimeout = 40 }, timed);
        Assert.Equal(TimeSpan.FromMilliseconds(40), server.Find(timed)!.ExpiresAfter);
        PortalBusTests.PumpUntil(host, () => closed.Count == 1);
        Assert.Equal(NotificationCloseReason.Expired, closed[0]);
        Assert.NotNull(server.Find(defaulted));
    }

    [Fact]
    public void Critical_expires_turns_the_critical_rule_off()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost();
        using var server = Start(host, daemon, new NotificationServerOptions { DefaultExpireMs = 40, CriticalExpires = true });
        var closed = new List<NotificationCloseReason>();
        server.Closed += (_, reason) => closed.Add(reason);

        server.Post(new Notification { Summary = "Battery low", Urgency = NotificationUrgency.Critical });
        server.Post(new Notification { Summary = "Battery lower", Urgency = NotificationUrgency.Critical, ExpireTimeout = 40 });
        PortalBusTests.PumpUntil(host, () => closed.Count == 2);
        Assert.All(closed, reason => Assert.Equal(NotificationCloseReason.Expired, reason));
        Assert.Empty(server.Active);
    }

    [Fact]
    public void Expiry_closes_with_reason_1_after_the_timeout_and_the_server_default()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost();
        using var server = Start(host, daemon, new NotificationServerOptions { DefaultExpireMs = 80 });
        using var client = NotificationTestClient.Connect(host, daemon.Address);

        var timed = Notify(host, client.NotifyAsync("timed", expireTimeout: 40));
        var defaulted = Notify(host, client.NotifyAsync("default", expireTimeout: -1));
        var never = Notify(host, client.NotifyAsync("never", expireTimeout: 0));
        Assert.Equal(TimeSpan.FromMilliseconds(80), server.Find(defaulted)!.ExpiresAfter);

        PortalBusTests.PumpUntil(host, () => client.Signals().Count == 2);
        var signals = client.Signals();
        Assert.Equal(("NotificationClosed", timed, "1"), signals[0]);
        Assert.Equal(("NotificationClosed", defaulted, "1"), signals[1]);
        Assert.Equal(never, Assert.Single(server.Active).Id);

        var expired = Notify(host, client.NotifyAsync("by hand", expireTimeout: 0));
        Assert.True(server.Expire(expired));
        PortalBusTests.PumpUntil(host, () => client.Signals().Count == 3);
        Assert.Equal(("NotificationClosed", expired, "1"), client.Signals()[2]);
    }

    [Fact]
    public void Invoke_sends_the_activation_token_before_the_action_and_closes_unless_resident()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost();
        using var server = Start(host, daemon);
        using var client = NotificationTestClient.Connect(host, daemon.Address);
        var invoked = new List<(uint Id, string Key, string? Token)>();
        server.ActionInvoked += (notification, key, token) => invoked.Add((notification.Id, key, token));

        var plain = Notify(host, client.NotifyAsync("plain", actions: ["default", "Open"], expireTimeout: 0));
        var resident = Notify(host, client.NotifyAsync("resident", actions: ["reply", "Reply"], expireTimeout: 0,
            hints: (ref MessageWriter writer) => NotificationTestClient.BoolHint(ref writer, "resident", true)));

        Assert.False(server.Invoke(plain, "missing", "token-0"));
        Assert.True(server.Invoke(plain, "default", "token-1"));
        PortalBusTests.PumpUntil(host, () => client.Signals().Count == 3);
        Assert.Equal(
            [("ActivationToken", plain, "token-1"), ("ActionInvoked", plain, "default"), ("NotificationClosed", plain, "2")],
            client.Signals());

        Assert.True(server.Invoke(resident, "reply"));
        PortalBusTests.PumpUntil(host, () => client.Signals().Count == 4);
        Assert.Equal(("ActionInvoked", resident, "reply"), client.Signals()[3]);
        Assert.Equal(resident, Assert.Single(server.Active).Id);
        Assert.Equal([(plain, "default", "token-1"), (resident, "reply", null)], invoked);
    }

    [Fact]
    public void Notification_server_fails_naming_the_owner_when_the_name_is_taken()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost();
        using var owner = new DBusConnection(daemon.Address);
        PortalBusTests.Await(host, owner.ConnectAsync().AsTask());
        Assert.True(PortalBusTests.Await(host, owner.TryRequestNameAsync(NotificationServer.BusName, RequestNameOptions.None)));

        var server = NotificationServer.TryCreate(host.Loop, new NotificationServerOptions { Address = daemon.Address }, out var reason);
        Assert.Null(server);
        Assert.NotNull(reason);
        Assert.Contains(owner.UniqueName!, reason);
        Assert.Contains(NotificationServer.BusName, reason);

        var replacing = NotificationServer.TryCreate(host.Loop, new NotificationServerOptions { Address = daemon.Address, Replace = true }, out reason);
        Assert.Null(replacing);
        Assert.Contains("does not allow replacement", reason);
    }

    [Fact]
    public void Notification_server_with_Replace_takes_the_name_over()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost();
        using var first = Start(host, daemon, new NotificationServerOptions { AllowReplacement = true });
        var lost = 0;
        first.NameLost += () => lost++;

        using var second = Start(host, daemon, new NotificationServerOptions { Replace = true });
        PortalBusTests.PumpUntil(host, () => lost == 1);
        Assert.False(first.OwnsName);
        Assert.True(second.OwnsName);

        using var client = NotificationTestClient.Connect(host, daemon.Address);
        var added = new List<Notification>();
        second.Added += added.Add;
        Notify(host, client.NotifyAsync("to the new owner", expireTimeout: 0));
        Assert.Single(added);
        Assert.Empty(first.Active);
    }

    [Fact]
    public void Notification_body_markup_is_reduced_to_text_and_spans()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost();
        using var server = Start(host, daemon);
        using var client = NotificationTestClient.Connect(host, daemon.Address);

        var id = Notify(host, client.NotifyAsync("markup", "<b>Tim</b> &amp; <i>Ana</i>: <a href=\"https://example.org/?a=1&amp;b=2\">link</a> 1 < 2", expireTimeout: 0));
        var notification = server.Find(id)!;
        Assert.Equal("Tim & Ana: link 1 < 2", notification.Body);
        Assert.Equal(
            [
                new NotificationSpan(0, 3, NotificationSpanKind.Bold),
                new NotificationSpan(6, 3, NotificationSpanKind.Italic),
                new NotificationSpan(11, 4, NotificationSpanKind.Link, "https://example.org/?a=1&b=2"),
            ],
            notification.BodySpans);
    }

    [Fact]
    public void Notification_image_data_is_copied_into_a_buffer_the_library_owns()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost();
        using var server = Start(host, daemon);
        using var client = NotificationTestClient.Connect(host, daemon.Address);
        var pixels = new byte[3 * 8 * 2];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (byte)(i * 7);
        }

        var id = Notify(host, client.NotifyAsync("image", expireTimeout: 0, hints: (ref MessageWriter writer) =>
        {
            NotificationTestClient.ImageHint(ref writer, "icon_data", 1, 1, 4, true, 4, [9, 9, 9, 9]);
            NotificationTestClient.ImageHint(ref writer, "image-data", 2, 3, 8, true, 4, pixels[..(8 * 3)]);
            NotificationTestClient.StringHint(ref writer, "image-path", "/usr/share/icons/x.png");
        }));
        var image = server.Find(id)!.Image;
        Assert.NotNull(image);
        Assert.Equal((2, 3, 8, true, 8, 4), (image.Width, image.Height, image.RowStride, image.HasAlpha, image.BitsPerSample, image.Channels));
        Assert.Equal(pixels[..(8 * 3)], image.Pixels.ToArray());
        Assert.Equal("/usr/share/icons/x.png", server.Find(id)!.ImagePath);

        var broken = Notify(host, client.NotifyAsync("short", expireTimeout: 0,
            hints: (ref MessageWriter writer) => NotificationTestClient.ImageHint(ref writer, "image-data", 4, 4, 16, true, 4, [1, 2, 3])));
        Assert.Null(server.Find(broken)!.Image);
    }

    [Fact]
    public void Notification_from_notify_send_is_read_and_its_action_reaches_it()
    {
        Assert.SkipUnless(PrivateBus.OnPath("notify-send"), "notify-send is not installed");
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost();
        using var server = Start(host, daemon);
        var added = new List<Notification>();
        server.Added += added.Add;

        var info = new System.Diagnostics.ProcessStartInfo("notify-send")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[] { "--app-name=libnotify-row", "--urgency=critical", "--category=im.received", "--hint=string:desktop-entry:org.example.Chat", "--action=open=Open", "--wait", "Ana", "<b>hi</b> there" })
        {
            info.ArgumentList.Add(argument);
        }

        info.Environment["DBUS_SESSION_BUS_ADDRESS"] = daemon.Address;
        using var client = System.Diagnostics.Process.Start(info)!;
        var output = client.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        try
        {
            PortalBusTests.PumpUntil(host, () => added.Count == 1);
            var notification = added[0];
            Assert.Equal("libnotify-row", notification.AppName);
            Assert.Equal("hi there", notification.Body);
            Assert.Equal(NotificationUrgency.Critical, notification.Urgency);
            Assert.Equal("im.received", notification.Category);
            Assert.Equal("org.example.Chat", notification.DesktopEntry);
            Assert.True(notification.HasAction("open"));

            Assert.True(server.Invoke(notification.Id, "open", "token-42"));
            PortalBusTests.PumpUntil(host, () => client.HasExited);
            Assert.Equal("open", PortalBusTests.Await(host, output).Trim());
        }
        finally
        {
            if (!client.HasExited)
            {
                client.Kill();
                client.WaitForExit(2000);
            }

            client.StandardOutput.Dispose();
        }
    }

    [Fact]
    public void Notification_posted_in_process_needs_no_client_and_sends_no_signal()
    {
        using var daemon = PrivateBus.Start();
        using var host = new CompositorTestHost();
        using var server = Start(host, daemon);
        using var client = NotificationTestClient.Connect(host, daemon.Address);
        var closed = new List<NotificationCloseReason>();
        server.Closed += (_, reason) => closed.Add(reason);

        var id = server.Post(new Notification { AppName = "luna-wm", Summary = "Battery low", Body = "10% left", ExpireTimeout = 0 });
        Assert.Equal("Battery low", server.Find(id)!.Summary);
        Assert.Null(server.Find(id)!.Sender);
        Assert.Equal(id, server.Post(new Notification { Summary = "Battery lower", ExpireTimeout = 0 }, id));
        Assert.Equal("Battery lower", Assert.Single(server.Active).Summary);

        var dbus = Notify(host, client.NotifyAsync("after", expireTimeout: 0));
        Assert.NotEqual(id, dbus);
        Assert.True(server.Dismiss(id));
        Assert.True(server.Dismiss(dbus));
        PortalBusTests.PumpUntil(host, () => client.Signals().Count == 1);
        for (var i = 0; i < 5; i++)
        {
            host.Loop.Dispatch(10);
        }

        Assert.Equal(("NotificationClosed", dbus, "2"), Assert.Single(client.Signals()));
        Assert.Equal([NotificationCloseReason.Dismissed, NotificationCloseReason.Dismissed], closed);
    }
}
