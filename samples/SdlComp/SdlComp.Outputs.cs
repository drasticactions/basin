using Basin;
using Basin.Hosted;
using Basin.Scene;
using Basin.Sdl;
using Basin.Shell.Nested;

namespace SdlComp;

internal sealed partial class SdlComp
{
    private readonly List<ExtraOutput> _extra = [];

    private sealed record ExtraOutput(SdlOutputWindow Window, SceneRect Background, string Key, SdlDrop Drop)
    {
        public int X { get; set; }
    }

    public IReadOnlyList<SdlOutputWindow> OutputWindows
    {
        get
        {
            var all = new List<SdlOutputWindow>(_extra.Count + 1);
            if (_window is not null)
            {
                all.Add(_window);
            }

            foreach (var extra in _extra)
            {
                all.Add(extra.Window);
            }

            if (_toplevels is not null)
            {
                foreach (var entry in _toplevels.Entries.Concat(_toplevels.Popups))
                {
                    if (entry.Window is { } toplevel)
                    {
                        all.Add(toplevel);
                    }
                }
            }

            return all;
        }
    }

    private void AddOutput(int number)
    {
        if (_shell is null)
        {
            return;
        }

        var key = $"SDL-{number}";
        var window = new SdlOutputWindow(_loop, _host, _device, new SdlOutputWindowOptions
        {
            Title = $"{Title} — {key}",
            Width = _options.Width,
            Height = _options.Height,
            OutputName = key,
            Scale = _options.Scale,
            GrabKeyboard = _options.GrabKeyboard,
        });
        var shell = _shell;
        var background = new SceneRect(shell.Layers.Background, 1, 1, shell.Background);
        var extra = new ExtraOutput(window, background, key, new SdlDrop(_host, _loop, window));
        _extra.Add(extra);
        window.InputSink = input => shell.HandleInput(input with { X = input.X + extra.X });
        window.CloseRequested += _ => RemoveOutput(extra);
        window.Reconfigured += ArrangeOutputs;
        _frames.Add(window);
        ArrangeOutputs(window);
        window.Window.Show();
    }

    private void RemoveOutput(ExtraOutput extra)
    {
        if (!_extra.Remove(extra))
        {
            return;
        }

        _frames.Remove(extra.Window);
        _host.Screens.Withdraw(extra.Key);
        extra.Drop.Dispose();
        extra.Background.Destroy();
        extra.Window.Dispose();
        if (_window is not null)
        {
            ArrangeOutputs(_window);
        }
    }

    private void ArrangeOutputs(SdlOutputWindow changed)
    {
        if (_shell is null)
        {
            return;
        }

        var x = _shell.Output.Width;
        foreach (var extra in _extra)
        {
            var view = extra.Window.View;
            var mode = view.Output.CurrentMode;
            var scale = view.Output.Scale;
            var width = NestedShell.Logical(mode.Width, scale);
            var height = NestedShell.Logical(mode.Height, scale);
            extra.X = x;
            view.SceneOutput.Position = new Point(x, 0);
            extra.Background.SetPosition(x, 0);
            extra.Background.Width = width;
            extra.Background.Height = height;
            _host.Screens.Advertise(view.Output, new HostScreenInfo(
                extra.Key, extra.Key, (int)Math.Round(x * scale), 0, mode.Width, mode.Height, scale, false));
            x += width;
        }
    }
}
