using System.Diagnostics;
using Basin;
using Basin.Backend.Headless;
using Basin.Capabilities;
using Basin.Cli;
using Basin.Desktop;
using Basin.Diagnostics;
using Basin.Render.Pixman;
using Basin.Scene;
using Basin.Shell.Xdg;
using Wayland.Server;

namespace Basin.Samples.Swap;

internal static class Program
{
    private static int Main(string[] args)
    {
        var cli = new BasinCommand("test program for checking seams.");
        var frames = cli.Add(CommonOptions.Frames(120));
        var client = cli.Add(CommonOptions.Client());
        var screenshot = cli.Add(CommonOptions.Screenshot("swap.png"));

        return cli.Run(args, result =>
        {
            cli.ConfigureLogging(result);
            var status = Run(
                result.GetValue(frames),
                result.GetValue(client),
                result.GetValue(screenshot)!,
                out var rendered);
            cli.ReportFrames(rendered);
            return status;
        });
    }

    private static int Run(long frames, string? clientCommand, string screenshotPath, out long renderedFrames)
    {
        BasinCounters.Reset();
        using var host = Basin.Host.BasinHost.Create(new Basin.Host.HostOptions());
        var display = host.Display;
        var socket = host.Socket;
        var loop = host.Loop;
        var output = host.Headless!.CreateOutput(new OutputMode(800, 600, 60_000));
        using var outputGlobal = new OutputGlobal(display, output);
        var layout = new OutputLayout();
        layout.Add(output, 0, 0);
        using var renderer = new PixmanRenderer();
        var scene = new Scene.Scene();
        var target = new MemoryBuffer(800, 600, DrmFormat.Xrgb8888);

        var capture = new TintedCapture(scene, layout, renderer);
        var clipboard = new HistoryClipboard();
        var bell = new CountingBell();

        using var services = host.CreateServices()
            .Use(layout)
            .Use<IScreenCapture>(capture)
            .Use<ISelectionStore>(clipboard)
            .Use<IBell>(bell)
            .Use<IActivationTokens>(new Basin.Capabilities.Defaults.DefaultActivationTokens())
            .Install(DesktopPack.Default)
            .Freeze();

        var shell = services.Require<XdgShell>();
        var placement = 40;
        shell.NewToplevel += toplevel => toplevel.Xdg.Mapped += () =>
        {
            var sceneSurface = new SceneSurface(scene.Root, toplevel.Surface);
            sceneSurface.Tree.SetPosition(placement, placement);
            placement += 60;
        };

        long rendered = 0;
        var running = true;
        var frameState = new OutputState();
        var frameClock = services.Require<Basin.Capabilities.IFrameClock>();
        output.Frame += () =>
        {
            frameClock.BeginFrameAtNextRefresh(output);
            scene.Render(renderer, target, new RenderColor(0.06f, 0.06f, 0.08f, 1f));
            frameState.Clear();
            output.Commit(frameState.SetBuffer(target));
            scene.SendFrameDone((uint)Environment.TickCount);

            capture.NotifyDamaged(output, new Box(0, 0, 800, 600));
            if (++rendered >= frames && frames > 0)
            {
                running = false;
            }
        };

        BasinReport.Line(Basin.Cli.CompositorLines.Socket(socket));
        using var client = Spawn(clientCommand, socket);
        while (running)
        {
            loop.Dispatch(16);
        }

        renderedFrames = rendered;

        var shot = new MemoryBuffer(800, 600, DrmFormat.Xrgb8888);
        var ok = capture.Capture(CaptureSource.Output(output), default, shot);
        if (ok)
        {
            BufferCapture.WritePng(shot, screenshotPath);
            BasinReport.Line($"SCREENSHOT {screenshotPath} (through {capture.GetType().Name})");
        }

        shot.Destroy();
        BasinDiagnostics.StopClient(client);
        target.Destroy();
        BasinReport.Line($"CAPTURES {capture.Captures} CLIPBOARD {clipboard.History} BELLS {bell.Rings}");
        return ok ? 0 : 1;
    }

    private static Process? Spawn(string? command, string socket)
    {
        if (command is null)
        {
            return null;
        }

        var parts = command.Split(' ', 2);
        var info = new ProcessStartInfo(parts[0]) { UseShellExecute = false };
        if (parts.Length > 1)
        {
            info.Arguments = parts[1];
        }

        info.Environment["WAYLAND_DISPLAY"] = socket;
        return Process.Start(info);
    }
}
