using Basin.Capabilities;
using Basin.Ipc;
using Basin.Scene;
using Xunit;

namespace Basin.Tests;

public sealed class IpcClientTests
{
    private static T Pump<T>(IpcTestRig rig, Task<T> task)
    {
        var deadline = Environment.TickCount64 + 10_000;
        while (!task.IsCompleted && Environment.TickCount64 < deadline)
        {
            rig.Host.Loop.Dispatch(5);
        }

        Assert.True(task.IsCompleted, "the call did not complete");
        return task.GetAwaiter().GetResult();
    }

    private static void Pump(IpcTestRig rig, Task task) => Pump(rig, task.ContinueWith(_ => 0, TaskScheduler.Default));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static (DirectoryInfo Directory, string Path) SocketPath()
    {
        var directory = System.IO.Directory.CreateTempSubdirectory("basin-ipc-client-");
        return (directory, System.IO.Path.Combine(directory.FullName, "basin-client.sock"));
    }

    [Fact]
    public void Typed_calls_pipeline_and_report_errors()
    {
        var (directory, path) = SocketPath();
        try
        {
            var model = new TestToplevelModel();
            using var rig = new IpcTestRig(services => services.Use<IToplevelModel>(model), path: path, listen: true);
            rig.Server.Start();
            var first = model.Add("one", "app.one", geometry: new Box(1, 2, 3, 4));
            using var client = Pump(rig, BasinIpcClient.ConnectAsync(path, Token));

            var version = client.VersionAsync(Token);
            var windows = client.ListWindowsAsync(Token);
            var missing = client.GetWindowAsync(99, Token);
            Assert.Equal("test", Pump(rig, version).Compositor);
            var listed = Pump(rig, windows);
            Assert.Equal("app.one", Assert.Single(listed).AppId);
            Assert.Equal(new IpcBox(1, 2, 3, 4), listed[0].Geometry);
            var failure = Assert.Throws<IpcCallException>(() => Pump(rig, missing));
            Assert.Equal(IpcErrorCodes.NotFound, failure.Code);

            var wait = client.WaitForWindowAsync(appId: "late", timeoutMs: 5000, cancellationToken: Token);
            var activate = client.ActivateAsync(first, Token);
            Pump(rig, activate);
            Assert.False(wait.IsCompleted);
            var late = model.Add("late", "late");
            Assert.Equal(late, Pump(rig, wait).Id);
            Assert.Contains(model.Requests, r => r.Kind == ToplevelRequestKind.Activate);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Descriptors_arrive_with_their_reply()
    {
        var (directory, path) = SocketPath();
        try
        {
            using var rig = new IpcTestRig(
                (services, host) =>
                {
                    services.Use(host.Layout);
                    services.Use<IScreenCapture>(new SceneScreenCapture(host.Scene, host.Layout) { Renderer = host.Renderer });
                },
                path: path,
                listen: true);
            rig.Server.Start();
            _ = MappedToplevel.Map(rig.Host, rig.Host.Client, 40, 30, 0xFF112233);
            rig.Host.PumpToClient();
            rig.Host.RenderFrame();
            using var client = Pump(rig, BasinIpcClient.ConnectAsync(path, Token));

            var calls = new List<Task>();
            var captures = new List<Task<IpcCapture>>();
            for (var i = 0; i < 20; i++)
            {
                calls.Add(client.VersionAsync(Token));
                var round = client.CaptureOutputAsync(null, IpcCaptureTarget.Fd, cancellationToken: Token);
                captures.Add(round);
                calls.Add(round);
                calls.Add(client.MethodsAsync(Token));
            }

            Pump(rig, Task.WhenAll(calls));
            foreach (var round in captures[..^1])
            {
                var extra = Pump(rig, round).Fd;
                Assert.NotNull(extra);
                extra!.Dispose();
            }

            var shot = Pump(rig, captures[^1]);
            Assert.NotNull(shot.Fd);
            using var fd = shot.Fd!;
            Assert.Equal(160, shot.Width);
            var bytes = new byte[shot.Stride * shot.Height];
            Assert.Equal(bytes.Length, RandomAccess.Read(fd, bytes, 0));
            Assert.Equal(rig.Host.Pixel(0, 0), BitConverter.ToUInt32(bytes, 0) | 0xFF000000u);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Subscribe_streams_events()
    {
        var (directory, path) = SocketPath();
        try
        {
            var model = new TestToplevelModel();
            using var rig = new IpcTestRig(services => services.Use<IToplevelModel>(model), path: path, listen: true);
            rig.Server.Start();
            var id = model.Add("start", "app");
            using var client = Pump(rig, BasinIpcClient.ConnectAsync(path, Token));
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
            var stream = client.SubscribeAsync(["window/changed"], cancel.Token).GetAsyncEnumerator(cancel.Token);
            var next = stream.MoveNextAsync().AsTask();
            var deadline = Environment.TickCount64 + 5000;
            while (model.ObserverCount == 0 && Environment.TickCount64 < deadline)
            {
                rig.Host.Loop.Dispatch(5);
            }

            model.Retitle(id, "renamed");
            Assert.True(Pump(rig, next));
            Assert.Equal("window/changed", stream.Current.Name);
            Assert.Contains("renamed", System.Text.Encoding.UTF8.GetString(stream.Current.Data), StringComparison.Ordinal);
            cancel.Cancel();
            Pump(rig, stream.DisposeAsync().AsTask());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void A_reply_followed_by_a_close_still_arrives()
    {
        var (directory, path) = SocketPath();
        try
        {
            using var rig = new IpcTestRig(path: path, listen: true);
            rig.Server.Methods.Register("test/bye", (ref IpcParams _, IpcReply reply) =>
            {
                reply.Result.WriteStartObject();
                reply.Result.WriteEndObject();
                rig.Host.Loop.AddIdle(() =>
                {
                    rig.Server.Dispose();
                });
            });
            rig.Server.Start();
            var fd = UnixSocket.Create();
            Assert.Equal(0, UnixSocket.Connect(fd, path));
            _ = UnixSocket.Close(fd);
            for (var i = 0; i < 3; i++)
            {
                rig.Host.Loop.Dispatch(5);
            }

            using var client = Pump(rig, BasinIpcClient.ConnectAsync(path, Token));
            using var result = Pump(rig, client.CallAsync("test/bye", cancellationToken: Token));
            Assert.Equal("{}", result.ToString());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
