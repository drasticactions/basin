using System.Diagnostics;
using Basin.Portal;
using Tmds.DBus.Protocol;
using Xunit;

namespace Basin.Tests;

public sealed class PortalProcessTests
{
    private static string? Locate(string name)
    {
        var directory = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && directory is not null; i++)
        {
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar));
        }

        return null;
    }

    private static Task<bool> OwnedAsync(DBusConnection connection)
    {
        var writer = connection.GetMessageWriter();
        try
        {
            writer.WriteMethodCallHeader("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "NameHasOwner", "s");
            writer.WriteString(PortalBus.DefaultBusName);
            return connection.CallMethodAsync(writer.CreateMessage(), static (Message m, object? _) => m.GetBodyReader().ReadBool(), null);
        }
        finally
        {
            writer.Dispose();
        }
    }

    private static Task<uint> DevicesAsync(DBusConnection connection)
    {
        var writer = connection.GetMessageWriter();
        try
        {
            writer.WriteMethodCallHeader(PortalBus.DefaultBusName, PortalBus.RootPath, "org.freedesktop.DBus.Properties", "Get", "ss");
            writer.WriteString("org.freedesktop.impl.portal.RemoteDesktop");
            writer.WriteString("AvailableDeviceTypes");
            return connection.CallMethodAsync(writer.CreateMessage(), static (Message m, object? _) => m.GetBodyReader().ReadVariantValue().GetUInt32(), null);
        }
        finally
        {
            writer.Dispose();
        }
    }

    [Theory]
    [InlineData("skia")]
    [InlineData("avalonia")]
    public async Task The_portal_runs_as_a_client_and_owns_its_name_against_TinyComp(string prompts)
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "the compositor half is Linux only");
        var compositor = Locate("tinycomp");
        Assert.SkipWhen(compositor is null, "tinycomp has not been built beside the tests");
        var portal = Locate("xdg-desktop-portal-basin");
        Assert.SkipWhen(portal is null, "xdg-desktop-portal-basin has not been built beside the tests");

        using var bus = PrivateBus.Start();
        using var compositorProcess = StartCompositor(compositor!, bus.Address, out var socket);
        Assert.SkipWhen(socket is null, "the compositor printed no socket");

        using var portalProcess = StartPortal(portal!, bus.Address, socket!, prompts);
        try
        {
            using var connection = new DBusConnection(bus.Address);
            await connection.ConnectAsync();
            var owned = false;
            for (var i = 0; i < 100 && !owned; i++)
            {
                owned = await OwnedAsync(connection);
                if (!owned)
                {
                    await Task.Delay(100, TestContext.Current.CancellationToken);
                }
            }

            Assert.True(owned, "the portal never owned its name");
            var devices = await DevicesAsync(connection);
            Assert.NotEqual(0u, devices & 3u);
        }
        finally
        {
            if (!portalProcess.HasExited)
            {
                portalProcess.Kill(entireProcessTree: true);
            }

            if (!compositorProcess.HasExited)
            {
                compositorProcess.Kill(entireProcessTree: true);
            }
        }
    }

    private static Process StartCompositor(string path, string busAddress, out string? socket)
    {
        var info = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add("--backend");
        info.ArgumentList.Add("headless");
        info.ArgumentList.Add("--renderer");
        info.ArgumentList.Add("pixman");
        info.ArgumentList.Add("--config");
        info.ArgumentList.Add("false");
        info.Environment["DBUS_SESSION_BUS_ADDRESS"] = busAddress;
        info.Environment["XDG_CURRENT_DESKTOP"] = "basin";
        var process = Process.Start(info)!;
        socket = null;
        for (var i = 0; i < 100; i++)
        {
            var line = process.StandardOutput.ReadLine();
            if (line is null)
            {
                break;
            }

            if (line.StartsWith("SOCKET ", StringComparison.Ordinal))
            {
                socket = line.Split(' ')[1];
                break;
            }
        }

        return process;
    }

    private static Process StartPortal(string path, string busAddress, string socket, string prompts)
    {
        var info = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        info.ArgumentList.Add("--socket");
        info.ArgumentList.Add(socket);
        info.ArgumentList.Add("--prompts");
        info.ArgumentList.Add(prompts);
        info.Environment["DBUS_SESSION_BUS_ADDRESS"] = busAddress;
        info.Environment["XDG_CURRENT_DESKTOP"] = "basin";
        var process = Process.Start(info)!;
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, _) => { };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }
}
