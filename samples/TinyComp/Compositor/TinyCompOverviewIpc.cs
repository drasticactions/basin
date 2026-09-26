using Basin.Host;
using Basin.Ipc;

namespace TinyComp;

internal sealed partial class TinyComp
{
    private void RegisterOverviewCommands(IpcMethodRegistry methods)
    {
        _report.Register(methods, "tinycomp/overview", "overview [{state:open|close}]", (ref IpcParams p, IpcReply reply) =>
        {
            bool? open = null;
            if (p.TryGetBool("open", out var flag))
            {
                open = flag;
            }
            else if (p.TryGetString("state", out var state))
            {
                open = state switch
                {
                    "open" => true,
                    "close" => false,
                    _ => null,
                };
                if (open is null)
                {
                    reply.Error(IpcErrorCodes.InvalidParams, "state is open or close");
                    return;
                }
            }

            OutputView? view = null;
            if (p.TryGetString("output", out var name))
            {
                view = Views.FirstOrDefault(v => v.Output.Name == name && v.Tag is OutputPolicy);
                if (view is null)
                {
                    reply.Error(IpcErrorCodes.NotFound, $"no output {name}");
                    return;
                }
            }

            if (p.Failed)
            {
                return;
            }

            view ??= OverviewTargetView();
            if (ToggleOverview(view, open) is null || view is null)
            {
                reply.Error(IpcErrorCodes.InvalidParams, "overview refused");
                return;
            }

            var json = reply.Result;
            json.WriteStartObject();
            json.WriteString("output", view.Output.Name);
            json.WriteBoolean("open", OverviewOf(view).Open);
            json.WriteNumber("progress", OverviewOf(view).Value);
            json.WriteEndObject();
        });

        _report.Register(methods, "tinycomp/shelve", "shelve {side:left|right|top|bottom} [{id:int}]", (ref IpcParams p, IpcReply reply) =>
        {
            var hasId = p.TryGetInt("id", out var id);
            var side = CanvasSide.None;
            var named = p.TryGetString("side", out var sideName);
            if (named)
            {
                side = SideOf(sideName);
                if (side == CanvasSide.None)
                {
                    reply.Error(IpcErrorCodes.InvalidParams, "side is left, right, top or bottom");
                    return;
                }
            }

            double? x = p.TryGetDouble("x", out var atX) ? atX : null;
            double? y = p.TryGetDouble("y", out var atY) ? atY : null;
            if (p.Failed)
            {
                return;
            }

            var window = hasId ? FindWindowByToplevelId((ulong)id) : ShelveTarget();
            if (window is null)
            {
                reply.Error(IpcErrorCodes.NotFound, hasId ? $"no window {id}" : "no focused window");
                return;
            }

            if (!named)
            {
                side = NearestShelfSide(window);
            }

            if (x is { } dropX && y is { } dropY && ViewOfWindow(window) is { Tag: OutputPolicy } view)
            {
                var (canvasX, canvasY) = view.Canvas.ToCanvasPoint(dropX, dropY);
                var box = CanvasBoxOf(window);
                window.MoveTo(
                    (int)Math.Round(canvasX - (box.Width / 2.0)) + (window.X - box.X),
                    (int)Math.Round(canvasY - (box.Height / 2.0)) + (window.Y - box.Y));
            }

            if (hasId)
            {
                Shelve(window, side);
            }
            else
            {
                ShelveChained(window, side);
            }

            var json = reply.Result;
            json.WriteStartObject();
            json.WriteNumber("id", hasId ? id : 0);
            json.WriteString("side", SideNames(side));
            json.WriteString("output", ViewOfWindow(window)?.Output.Name ?? string.Empty);
            json.WriteBoolean("shelved", IsShelved(window));
            json.WriteEndObject();
        });

        _report.Register(methods, "tinycomp/unshelve", "unshelve [{id:int}]", (ref IpcParams p, IpcReply reply) =>
        {
            var hasId = p.TryGetInt("id", out var id);
            double? x = p.TryGetDouble("x", out var atX) ? atX : null;
            double? y = p.TryGetDouble("y", out var atY) ? atY : null;
            if (p.Failed)
            {
                return;
            }

            if (!hasId)
            {
                UnshelveByKey();
                return;
            }

            if (FindWindowByToplevelId((ulong)id) is not { } window)
            {
                reply.Error(IpcErrorCodes.NotFound, $"no window {id}");
                return;
            }

            if (!IsShelved(window))
            {
                reply.Error(IpcErrorCodes.InvalidParams, "the window is not shelved");
                return;
            }

            var owner = ShelfOwnerOf(window);
            Unshelve(window, x, y);
            var json = reply.Result;
            json.WriteStartObject();
            json.WriteNumber("id", id);
            json.WriteString("output", owner?.Output.Name ?? string.Empty);
            json.WriteNumber("x", window.X);
            json.WriteNumber("y", window.Y);
            json.WriteEndObject();
        });

        _report.Register(methods, "tinycomp/shelves", "shelves", (ref IpcParams p, IpcReply reply) =>
        {
            OutputView? only = null;
            if (p.TryGetString("output", out var name))
            {
                only = Views.FirstOrDefault(v => v.Output.Name == name && v.Tag is OutputPolicy);
                if (only is null)
                {
                    reply.Error(IpcErrorCodes.NotFound, $"no output {name}");
                    return;
                }
            }

            if (p.Failed)
            {
                return;
            }

            ReportShelvedWindows();
            WriteShelvesJson(reply.Result, only);
        });
    }

    private static CanvasSide SideOf(string name) => name switch
    {
        "left" => CanvasSide.Left,
        "right" => CanvasSide.Right,
        "top" => CanvasSide.Top,
        "bottom" => CanvasSide.Bottom,
        _ => CanvasSide.None,
    };

    private CanvasSide NearestShelfSide(IGrabTarget window)
    {
        if (ViewOfWindow(window) is not { Tag: OutputPolicy } view)
        {
            return CanvasSide.Right;
        }

        var box = CanvasBoxOf(window);
        var output = _layout.BoxOf(view.Output);
        var centerX = box.X + (box.Width / 2.0);
        var centerY = box.Y + (box.Height / 2.0);
        _ = IsOverviewSide(view, CanvasSide.Left);
        var sides = OverviewOf(view).Sides;
        var best = CanvasSide.Right;
        var least = double.PositiveInfinity;
        foreach (var side in CanvasSides.Each)
        {
            if (!sides.HasFlag(side))
            {
                continue;
            }

            var distance = side switch
            {
                CanvasSide.Left => centerX - output.X,
                CanvasSide.Right => output.Right - centerX,
                CanvasSide.Top => centerY - output.Y,
                _ => output.Bottom - centerY,
            };
            if (distance < least)
            {
                least = distance;
                best = side;
            }
        }

        return best;
    }

    private void CloseRefusedOverviews()
    {
        for (var i = 0; i < Views.Count; i++)
        {
            var view = Views[i];
            if (view.Tag is OutputPolicy && OverviewOf(view).Active &&
                (CanvasWanted(view) || !_config.OverviewFor(view.Output.Name).Enabled))
            {
                var overview = OverviewOf(view);
                overview.Open = false;
                overview.Tracking = false;
                overview.Progress.Cancel();
                overview.Value = 0;
                FinishOverview(view);
                EmitOverviewChanged(view);
            }
        }
    }
}
