using System.Text;
using System.Text.Json;
using Basin.Ipc;

using Xunit;

namespace Basin.Tests;

public sealed class IpcServerTests
{
    private static JsonElement Parse(string frame) => JsonDocument.Parse(frame).RootElement.Clone();

    [Fact]
    public void Version_reports_protocol_and_compositor()
    {
        using var rig = new IpcTestRig(session: new IpcSessionInfo { Compositor = "tinycomp" });
        var peer = rig.Connect();
        var reply = Parse(peer.Call("""{"id":1,"method":"ipc/version"}"""));
        Assert.Equal(1, reply.GetProperty("id").GetInt32());
        var result = reply.GetProperty("result");
        Assert.Equal(IpcProtocol.Version, result.GetProperty("protocol").GetInt32());
        Assert.Equal("tinycomp", result.GetProperty("compositor").GetString());
    }

    [Fact]
    public void Id_echoes_as_number_string_and_absence()
    {
        using var rig = new IpcTestRig();
        var peer = rig.Connect();
        Assert.Contains("\"id\":12345678901234", peer.Call("""{"id":12345678901234,"method":"ipc/version"}"""), StringComparison.Ordinal);
        Assert.Contains("\"id\":\"a\\\"b\"", peer.Call("""{"id":"a\"b","method":"ipc/version"}"""), StringComparison.Ordinal);
        var bare = Parse(peer.Call("""{"method":"ipc/version"}"""));
        Assert.False(bare.TryGetProperty("id", out _));
        Assert.True(bare.TryGetProperty("result", out _));
    }

    [Fact]
    public void Split_header_and_split_body_assemble()
    {
        using var rig = new IpcTestRig();
        var peer = rig.Connect();
        var frame = IpcTestPeer.Frame("""{"id":2,"method":"ipc/version"}""");
        peer.SendRaw(frame.AsSpan(0, 2));
        rig.Pump();
        Assert.False(peer.HasFrame());
        peer.SendRaw(frame.AsSpan(2, 9));
        rig.Pump();
        Assert.False(peer.HasFrame());
        peer.SendRaw(frame.AsSpan(11));
        Assert.Equal(2, Parse(peer.Receive()).GetProperty("id").GetInt32());
    }

    [Fact]
    public void Two_frames_in_one_write_both_answer_in_order()
    {
        using var rig = new IpcTestRig();
        var peer = rig.Connect();
        var both = IpcTestPeer.Frame("""{"id":1,"method":"ipc/version"}""")
            .Concat(IpcTestPeer.Frame("""{"id":2,"method":"ipc/methods"}""")).ToArray();
        peer.SendRaw(both);
        Assert.Equal(1, Parse(peer.Receive()).GetProperty("id").GetInt32());
        Assert.Equal(2, Parse(peer.Receive()).GetProperty("id").GetInt32());
    }

    [Fact]
    public void Malformed_body_is_parse_error_and_the_connection_survives()
    {
        using var rig = new IpcTestRig();
        var peer = rig.Connect();
        var reply = Parse(peer.Call("{not json"));
        Assert.Equal(IpcErrorCodes.ParseError, reply.GetProperty("error").GetProperty("code").GetString());
        var array = Parse(peer.Call("[1,2]"));
        Assert.Equal(IpcErrorCodes.ParseError, array.GetProperty("error").GetProperty("code").GetString());
        var noMethod = Parse(peer.Call("""{"id":4}"""));
        Assert.Equal(4, noMethod.GetProperty("id").GetInt32());
        Assert.Equal(IpcErrorCodes.ParseError, noMethod.GetProperty("error").GetProperty("code").GetString());
        Assert.True(Parse(peer.Call("""{"method":"ipc/version"}""")).TryGetProperty("result", out _));
    }

    [Fact]
    public void Oversized_header_closes_the_connection()
    {
        using var rig = new IpcTestRig();
        var peer = rig.Connect();
        var header = new byte[4];
        IpcProtocol.WriteLength(header, IpcProtocol.MaxRequestBytes + 1);
        peer.SendRaw(header);
        Assert.True(peer.WaitClosed());
        Assert.Equal(0, rig.Server.ConnectionCount);
    }

    [Fact]
    public void Request_of_exactly_the_limit_is_read()
    {
        using var rig = new IpcTestRig();
        var peer = rig.Connect();
        var prefix = """{"id":9,"method":"ipc/version","pad":" """;
        var suffix = "\"}";
        var padding = IpcProtocol.MaxRequestBytes - Encoding.UTF8.GetByteCount(prefix) - suffix.Length;
        var json = prefix + new string('x', padding) + suffix;
        Assert.Equal(IpcProtocol.MaxRequestBytes, Encoding.UTF8.GetByteCount(json));
        Assert.Equal(9, Parse(peer.Call(json)).GetProperty("id").GetInt32());
    }

    [Fact]
    public void Unknown_method_names_it_and_keeps_the_connection()
    {
        using var rig = new IpcTestRig();
        var peer = rig.Connect();
        var reply = Parse(peer.Call("""{"id":1,"method":"nope/nothing"}"""));
        var error = reply.GetProperty("error");
        Assert.Equal(IpcErrorCodes.UnknownMethod, error.GetProperty("code").GetString());
        Assert.Contains("nope/nothing", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.True(Parse(peer.Call("""{"method":"ipc/version"}""")).TryGetProperty("result", out _));
    }

    [Fact]
    public void Registration_rules_hold()
    {
        using var rig = new IpcTestRig();
        IpcHandler handler = (ref IpcParams _, IpcReply _) => { };
        rig.Server.Methods.Register("test/one", handler);
        Assert.Throws<InvalidOperationException>(() => rig.Server.Methods.Register("test/one", handler));
        Assert.Throws<ArgumentException>(() => rig.Server.Methods.Register("windows/mine", handler));
        Assert.Throws<ArgumentException>(() => rig.Server.Methods.Register("Test/Upper", handler));
        Assert.Throws<ArgumentException>(() => rig.Server.Events.Declare("window/mine"));
        rig.Server.Start();
        Assert.Throws<InvalidOperationException>(() => rig.Server.Methods.Register("test/two", handler));
        Assert.Contains("test/one", rig.Server.Methods.Names);
    }

    [Fact]
    public void Namespaced_method_reads_params_and_rejects_bad_ones()
    {
        using var rig = new IpcTestRig();
        rig.Server.Methods.Register("test/add", (ref IpcParams parameters, IpcReply reply) =>
        {
            var a = parameters.GetInt("a");
            var b = parameters.GetInt("b");
            if (parameters.Failed)
            {
                return;
            }

            reply.Result.WriteStartObject();
            reply.Result.WriteNumber("sum", a + b);
            reply.Result.WriteEndObject();
        });
        var peer = rig.Connect();
        Assert.Equal(5, Parse(peer.Call("""{"method":"test/add","params":{"a":2,"b":3}}""")).GetProperty("result").GetProperty("sum").GetInt32());
        var missing = Parse(peer.Call("""{"method":"test/add","params":{"a":2}}"""));
        Assert.Equal(IpcErrorCodes.InvalidParams, missing.GetProperty("error").GetProperty("code").GetString());
        var wrong = Parse(peer.Call("""{"method":"test/add","params":{"a":2,"b":"x"}}"""));
        Assert.Contains("'b'", wrong.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Handler_exception_is_internal_and_the_connection_survives()
    {
        var warm = new IpcMethodRegistry();
        warm.Register("warm/throw", (ref IpcParams _, IpcReply _) => throw new InvalidOperationException("warm"));
        _ = warm.TryInvoke("warm/throw", default, new IpcReply(new NullSink()));
        using var rig = new IpcTestRig();
        rig.Server.Methods.Register("test/throw", (ref IpcParams _, IpcReply reply) =>
        {
            reply.Result.WriteStartObject();
            throw new InvalidOperationException("boom");
        });
        var peer = rig.Connect();
        var reply = Parse(peer.Call("""{"method":"test/throw"}"""));
        Assert.Equal(IpcErrorCodes.Internal, reply.GetProperty("error").GetProperty("code").GetString());
        Assert.True(Parse(peer.Call("""{"method":"ipc/version"}""")).TryGetProperty("result", out _));
    }

    [Fact]
    public void Deferred_reply_is_overtaken_by_a_later_one()
    {
        using var rig = new IpcTestRig();
        IpcPendingReply? pending = null;
        rig.Server.Methods.Register("test/later", (ref IpcParams _, IpcReply reply) => pending = reply.Defer());
        var peer = rig.Connect();
        peer.Send("""{"id":"slow","method":"test/later"}""");
        peer.Send("""{"id":"fast","method":"ipc/version"}""");
        Assert.Equal("fast", Parse(peer.Receive()).GetProperty("id").GetString());
        Assert.NotNull(pending);
        pending!.Result.WriteStartObject();
        pending.Result.WriteString("done", "yes");
        pending.Result.WriteEndObject();
        Assert.True(pending.Complete());
        var slow = Parse(peer.Receive());
        Assert.Equal("slow", slow.GetProperty("id").GetString());
        Assert.Equal("yes", slow.GetProperty("result").GetProperty("done").GetString());
    }

    [Fact]
    public void Deferred_reply_to_a_closed_connection_is_dropped()
    {
        using var rig = new IpcTestRig();
        IpcPendingReply? pending = null;
        rig.Server.Methods.Register("test/later", (ref IpcParams _, IpcReply reply) => pending = reply.Defer());
        var peer = rig.Connect();
        peer.Send("""{"method":"test/later"}""");
        for (var i = 0; i < 20 && pending is null; i++)
        {
            rig.Pump();
        }

        peer.Dispose();
        for (var i = 0; i < 20 && rig.Server.ConnectionCount > 0; i++)
        {
            rig.Pump();
        }

        Assert.False(pending!.Complete());
        Assert.False(pending.Completed);
    }

    [Fact]
    public void Session_describe_and_quit()
    {
        var quit = false;
        using var rig = new IpcTestRig(session: new IpcSessionInfo
        {
            Compositor = "t",
            Backend = "headless",
            Renderer = "pixman",
            WaylandSocket = "wayland-9",
            Quit = () => quit = true,
        });
        var peer = rig.Connect();
        var result = Parse(peer.Call("""{"method":"session/describe"}""")).GetProperty("result");
        Assert.Equal("headless", result.GetProperty("backend").GetString());
        Assert.Equal("wayland-9", result.GetProperty("wayland_socket").GetString());
        Assert.True(Parse(peer.Call("""{"method":"session/quit"}""")).TryGetProperty("result", out _));
        Assert.True(quit);
    }

    [Fact]
    public void Quit_is_absent_without_a_stop_action()
    {
        using var rig = new IpcTestRig();
        rig.Server.Start();
        Assert.DoesNotContain(IpcMethodNames.SessionQuit, rig.Server.Methods.Names);
    }

    [Fact]
    public void Teardown_with_connections_open_leaves_nothing()
    {
        var rig = new IpcTestRig();
        _ = rig.Connect();
        _ = rig.Connect();
        Assert.Equal(2, rig.Server.ConnectionCount);
        rig.Dispose();
    }

    [Fact]
    public void Socket_binds_0600_reports_and_unlinks()
    {
        var directory = Directory.CreateTempSubdirectory("basin-ipc-");
        try
        {
            var path = Path.Combine(directory.FullName, "basin-test.sock");
            var previous = Environment.GetEnvironmentVariable(IpcProtocol.SocketVariable);
            using (var rig = new IpcTestRig(path: path, listen: true))
            {
                rig.Server.Start();
                Assert.Equal(path, rig.Server.Path);
                Assert.Equal(path, Environment.GetEnvironmentVariable(IpcProtocol.SocketVariable));
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));

                var fd = UnixSocket.Create();
                Assert.Equal(0, UnixSocket.Connect(fd, path));
                using var peer = new IpcTestPeer(fd, rig.Pump);
                rig.Pump();
                Assert.Equal(1, rig.Server.ConnectionCount);
                Assert.True(Parse(peer.Call("""{"method":"ipc/version"}""")).TryGetProperty("result", out _));

                using var second = new IpcServer(rig.Host.Loop, rig.Services, new IpcSessionInfo { Compositor = "b" }, path);
                second.Start();
                Assert.Null(second.Path);
            }

            Assert.False(File.Exists(path));
            Assert.Equal(previous, Environment.GetEnvironmentVariable(IpcProtocol.SocketVariable));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Stale_socket_file_is_replaced()
    {
        var directory = Directory.CreateTempSubdirectory("basin-ipc-");
        try
        {
            var path = Path.Combine(directory.FullName, "basin-stale.sock");
            var stale = UnixSocket.Create();
            Assert.Equal(0, UnixSocket.Bind(stale, path));
            _ = UnixSocket.Close(stale);
            Assert.True(File.Exists(path));
            using var rig = new IpcTestRig(path: path, listen: true);
            rig.Server.Start();
            Assert.Equal(path, rig.Server.Path);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Connection_from_another_uid_is_closed()
    {
        Assert.SkipWhen(UnixSocket.EffectiveUid() != 0, "only root can connect as another uid");
        Assert.SkipUnless(File.Exists("/usr/bin/setpriv") && File.Exists("/usr/bin/python3"), "needs setpriv and python3");
        using (var warm = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/bin/true")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!)
        {
            warm.WaitForExit();
        }

        var directory = Directory.CreateTempSubdirectory("basin-ipc-");
        try
        {
            File.SetUnixFileMode(directory.FullName, (UnixFileMode)Convert.ToInt32("777", 8));
            var path = Path.Combine(directory.FullName, "basin-uid.sock");
            using var rig = new IpcTestRig(path: path, listen: true);
            rig.Server.Start();
            File.SetUnixFileMode(path, (UnixFileMode)Convert.ToInt32("666", 8));
            var script = "import socket;s=socket.socket(socket.AF_UNIX);s.connect('" + path + "');"
                + "print('closed' if s.recv(4)==b'' else 'answered')";
            string said;
            using (var child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "/usr/bin/setpriv", ["--reuid=65534", "--regid=65534", "--clear-groups", "/usr/bin/python3", "-c", script])
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = "/",
            })!)
            {
                while (!child.HasExited)
                {
                    rig.Pump();
                    Thread.Sleep(5);
                }

                said = child.StandardOutput.ReadToEnd().Trim() + child.StandardError.ReadToEnd().Trim();
                child.StandardOutput.Close();
                child.StandardError.Close();
            }

            Assert.True(said == "closed", said);
            Assert.Equal(0, rig.Server.ConnectionCount);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class NullSink : IIpcReplySink
    {
        public bool IsOpen => true;

        public IpcClientState State { get; } = new();

        public void Deliver(IpcReply reply)
        {
        }
    }
}
