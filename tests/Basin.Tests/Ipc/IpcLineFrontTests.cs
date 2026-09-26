using Basin.Ipc;
using Xunit;

namespace Basin.Tests;

public sealed class IpcLineFrontTests
{
    private static List<string> Run(IpcServer server, params string[] lines)
    {
        var previous = Console.Out;
        var captured = new StringWriter();
        Console.SetOut(captured);
        try
        {
            foreach (var line in lines)
            {
                server.LineFront!.Execute(line);
            }
        }
        finally
        {
            Console.SetOut(previous);
        }

        return [.. captured.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r'))];
    }

    [Fact]
    public void Line_forms_bind_positional_words_and_render_replies()
    {
        using var rig = new IpcTestRig();
        var report = new IpcLineReport();
        var seen = new List<string>();
        report.Register(rig.Server.Methods, "test/move", "move {x:number} {y:number}", (ref IpcParams p, IpcReply _) =>
            seen.Add(FormattableString.Invariant($"{p.GetDouble("x")},{p.GetDouble("y")}")));
        report.Register(rig.Server.Methods, "test/where", "where", () =>
        {
            report.Line("POINTER 1 2");
            report.Line("WINDOW \"a b\" 0,0 ws=1");
        });
        report.Register(rig.Server.Methods, "test/side", "park {side:left|right}", (ref IpcParams p, IpcReply _) => seen.Add(p.GetString("side")));
        report.Register(rig.Server.Methods, "test/clock", "clock {text...}", (ref IpcParams p, IpcReply _) => seen.Add(p.GetString("text")));
        report.Register(rig.Server.Methods, "test/shot", "shot {path} [{index:int}]", (ref IpcParams p, IpcReply reply) =>
        {
            var index = p.TryGetInt("index", out var at) ? at : 0;
            if (!p.Failed)
            {
                reply.Error(IpcErrorCodes.NotFound, $"no output {index}");
            }
        });
        rig.Server.Start();
        rig.Server.StartLineFront(-1);

        var output = Run(rig.Server, "move 1.5 -2", "where", "park left", "park up", "clock 12:30 pm", "shot /x 3", "move 1 nope", "bogus", "ipc ipc/version", "ipc nope/nothing", "ipc test/where {bad", "");
        Assert.Equal(["1.5,-2", "left", "12:30 pm"], seen);
        Assert.Equal("POINTER 1 2", output[0]);
        Assert.Equal("WINDOW \"a b\" 0,0 ws=1", output[1]);
        Assert.Equal("ERR invalid_params usage: park {side:left|right}", output[2]);
        Assert.Equal("ERR no output 3", output[3]);
        Assert.Equal("ERR invalid_params 'y' must be a number, not 'nope'", output[4]);
        Assert.Equal("ERR unknown_method bogus", output[5]);
        Assert.StartsWith("OK ipc/version protocol=1 compositor=test", output[6], StringComparison.Ordinal);
        Assert.Equal("ERR unknown_method no method 'nope/nothing' on this compositor", output[7]);
        Assert.Equal("ERR parse_error the params are not a JSON object", output[8]);
        Assert.Equal(9, output.Count);
    }

    [Fact]
    public void A_socket_client_gets_the_lines_the_front_prints()
    {
        using var rig = new IpcTestRig();
        var report = new IpcLineReport();
        report.Register(rig.Server.Methods, "test/where", "where", () => report.Line("POINTER 1 2"));
        var peer = rig.Connect();
        var reply = peer.Call("""{"method":"test/where"}""");
        Assert.Equal("""{"result":{"lines":["POINTER 1 2"]}}""", reply);
    }

    [Fact]
    public void A_deferred_line_reply_prints_when_it_completes()
    {
        using var rig = new IpcTestRig();
        var report = new IpcLineReport();
        IpcPendingReply? pending = null;
        report.Register(rig.Server.Methods, "test/later", "later", (ref IpcParams _, IpcReply reply) => pending = reply.Defer());
        rig.Server.Start();
        rig.Server.StartLineFront(-1);
        Assert.Empty(Run(rig.Server, "later"));
        var previous = Console.Out;
        var captured = new StringWriter();
        Console.SetOut(captured);
        try
        {
            Assert.True(IpcLineReport.Complete(pending!, "SHOT /tmp/x.png"));
        }
        finally
        {
            Console.SetOut(previous);
        }

        Assert.Equal("SHOT /tmp/x.png", captured.ToString().Trim());
    }
}
