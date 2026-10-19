using Basin.Host;
using Basin.Ipc;

namespace Basin.Inspector.Ipc;

public static class InspectorIpc
{
    public static void Register(IpcMethodRegistry methods, IpcLineReport report, InspectorIpcOptions options)
    {
        ArgumentNullException.ThrowIfNull(methods);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(options);

        report.Register(methods, "inspector/open", "inspector open [{output}]", (ref IpcParams p, IpcReply _) =>
        {
            var name = p.TryGetString("output", out var output) ? output : null;
            if (Resolve(options, report, name) is not ({ } overlay, { } view))
            {
                return;
            }

            if (!overlay.Open(view))
            {
                report.Line($"ERR the inspector could not be placed on {view.Output.Name}");
                return;
            }

            State(options, report);
        });

        report.Register(methods, "inspector/close", "inspector close", () =>
        {
            options.Overlay()?.Close();
            State(options, report);
        });

        report.Register(methods, "inspector/toggle", "inspector toggle [{output}]", (ref IpcParams p, IpcReply _) =>
        {
            var name = p.TryGetString("output", out var output) ? output : null;
            if (Resolve(options, report, name) is not ({ } overlay, { } view))
            {
                return;
            }

            overlay.Toggle(view);
            State(options, report);
        });

        report.Register(methods, "inspector/move", "inspector-output {output}", (ref IpcParams p, IpcReply _) =>
        {
            var name = p.GetString("output");
            if (p.Failed || Resolve(options, report, name) is not ({ } overlay, { } view))
            {
                return;
            }

            overlay.MoveTo(view);
            State(options, report);
        });

        report.Register(methods, "inspector/panel", "inspector-panel {name} [{open:on|off}]", (ref IpcParams p, IpcReply _) =>
        {
            var name = p.GetString("name");
            var open = !p.TryGetString("open", out var state) || state != "off";
            if (p.Failed)
            {
                return;
            }

            if (options.Overlay() is not { } overlay)
            {
                report.Line($"ERR the inspector is unavailable: {options.Declined?.Invoke() ?? "no ImGui host"}");
                return;
            }

            if (!overlay.Show(name, open))
            {
                report.Line($"ERR no inspector panel {name}");
                return;
            }

            State(options, report);
        });

        report.Register(methods, "inspector/state", "inspector-state", () => State(options, report));
    }

    private static (InspectorOverlay?, OutputView?) Resolve(InspectorIpcOptions options, IpcLineReport report, string? name)
    {
        if (options.Overlay() is not { } overlay)
        {
            report.Line($"ERR the inspector is unavailable: {options.Declined?.Invoke() ?? "no ImGui host"}");
            return (null, null);
        }

        OutputView? view = null;
        if (string.IsNullOrEmpty(name))
        {
            view = overlay.Output ?? options.DefaultOutput?.Invoke() ?? options.Driver.Views.FirstOrDefault();
        }
        else
        {
            foreach (var candidate in options.Driver.Views)
            {
                if (string.Equals(candidate.Output.Name, name, StringComparison.Ordinal))
                {
                    view = candidate;
                    break;
                }
            }
        }

        if (view is null)
        {
            report.Line($"ERR no output {name ?? "to open on"}");
            return (overlay, null);
        }

        return (overlay, view);
    }

    public static string Describe(InspectorOverlay? overlay, string? declined)
    {
        if (overlay is null)
        {
            return $"INSPECTOR open=no host=none declined=\"{declined ?? "no ImGui host"}\"";
        }

        var panels = new List<string>();
        foreach (var panel in overlay.Panels)
        {
            if (overlay.IsShown(panel.Name))
            {
                panels.Add(System.Text.Encoding.UTF8.GetString(panel.Name));
            }
        }

        return $"INSPECTOR open={(overlay.IsOpen ? "yes" : "no")} output={overlay.Output?.Output.Name ?? "none"} host={overlay.HostName} panels={(panels.Count == 0 ? "none" : string.Join(',', panels))}";
    }

    private static void State(InspectorIpcOptions options, IpcLineReport report) =>
        report.Line(Describe(options.Overlay(), options.Declined?.Invoke()));
}
