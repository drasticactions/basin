using Basin.Diagnostics;
using Basin.Host;
using Xunit;

namespace Basin.Tests;

public sealed class InitProcessTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"basin-init-{Environment.ProcessId}-{Guid.NewGuid():N}");
    private readonly string? _saved = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
    private readonly BasinLogger _log = BasinLog.For("InitProcessTests");

    public InitProcessTests()
    {
        Directory.CreateDirectory(_root);
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _root);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _saved);
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Stopping_a_group_that_already_exited_logs_no_error()
    {
        using var host = new CompositorTestHost();
        var sink = new ErrorSink();
        var previousSink = BasinLog.Sink;
        var previousLevel = BasinLog.Level;
        BasinLog.Sink = sink;
        BasinLog.Level = BasinLogLevel.Trace;
        try
        {
            var init = InitProcess.Start("sleep 0.2", "wayland-none", null, _log);
            Assert.NotNull(init);
            Assert.True(init!.WatchExit(host.Loop));
            var deadline = Environment.TickCount64 + 5000;
            while (init.ExitStatus is null && Environment.TickCount64 < deadline)
            {
                host.Loop.Dispatch(50);
            }

            Assert.NotNull(init.ExitStatus);

            init.Stop(_log);
            Assert.Empty(sink.Errors);
        }
        finally
        {
            BasinLog.Sink = previousSink;
            BasinLog.Level = previousLevel;
        }
    }

    private sealed class ErrorSink : IBasinLogSink
    {
        public List<string> Errors { get; } = [];

        public void Write(BasinLogLevel level, string category, ReadOnlySpan<char> message)
        {
            if (level == BasinLogLevel.Error)
            {
                Errors.Add(message.ToString());
            }
        }
    }

    [Fact]
    public void No_init_file_finds_nothing_and_is_not_an_error()
    {
        var search = InitProcess.Find(["first", "second"], _log);

        Assert.Null(search.Path);
        Assert.False(search.NotExecutable);
    }

    [Fact]
    public void An_executable_init_in_the_second_directory_is_found()
    {
        var path = Write("second", UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var search = InitProcess.Find(["first", "second"], _log);

        Assert.Equal(path, search.Path);
        Assert.False(search.NotExecutable);
    }

    [Fact]
    public void An_init_that_is_not_executable_stops_the_search()
    {
        Write("first", UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Write("second", UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        var search = InitProcess.Find(["first", "second"], _log);

        Assert.Null(search.Path);
        Assert.True(search.NotExecutable);
    }

    private string Write(string directory, UnixFileMode mode)
    {
        var folder = Path.Combine(_root, directory);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "init");
        File.WriteAllText(path, "#!/bin/sh\n");
        File.SetUnixFileMode(path, mode);
        return path;
    }
}
