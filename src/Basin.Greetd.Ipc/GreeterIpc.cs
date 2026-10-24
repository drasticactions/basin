using Basin.Ipc;

namespace Basin.Greetd.Ipc;

/// <summary>
/// Registers the <c>greeter/*</c> control socket methods that drive a <see cref="GreeterFlow"/>, each with its stdin word.
/// </summary>
public static class GreeterIpc
{
    /// <summary>
    /// Registers <c>users</c>, <c>sessions</c>, <c>select</c>, <c>other</c>, <c>answer</c>, <c>cancel</c>, <c>dismiss</c>, <c>session</c>, <c>layout</c>, <c>command</c> and <c>state</c> under <c>greeter/</c>.
    /// A refused request prints an <c>ERR</c> line. A <c>session</c> or <c>layout</c> counts as a choice by the user.
    /// </summary>
    /// <param name="methods">The registry that receives the methods.</param>
    /// <param name="report">The line report that prints each method's output.</param>
    /// <param name="flow">The flow that the methods drive.</param>
    public static void Register(IpcMethodRegistry methods, IpcLineReport report, GreeterFlow flow)
    {
        ArgumentNullException.ThrowIfNull(methods);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(flow);

        report.Register(methods, "greeter/users", "users", () =>
        {
            foreach (var user in flow.Users)
            {
                report.Line($"USER {user.Name} uid={user.Uid} name=\"{user.DisplayName}\" icon={(user.IconPath is null ? "no" : "yes")}");
            }

            report.Line($"USERS {flow.Users.Count}");
        });

        report.Register(methods, "greeter/sessions", "sessions", () =>
        {
            foreach (var session in flow.Sessions)
            {
                report.Line($"SESSION {session.Id} type={session.Type.ToString().ToLowerInvariant()} name=\"{session.Name}\"");
            }

            report.Line($"SESSIONS {flow.Sessions.Count}");
        });

        report.Register(methods, "greeter/select", "select {user}", (ref IpcParams p, IpcReply _) =>
        {
            var name = p.GetString("user");
            if (!p.Failed && !flow.Choose(name))
            {
                report.Line($"ERR no listed user {name}");
            }
        });

        report.Register(methods, "greeter/other", "other {user}", (ref IpcParams p, IpcReply _) =>
        {
            var name = p.GetString("user");
            if (!p.Failed)
            {
                flow.EnterName(name);
            }
        });

        report.Register(methods, "greeter/answer", "answer {text...}", (ref IpcParams p, IpcReply _) =>
        {
            var text = p.GetString("text");
            if (!p.Failed && !flow.Answer(text))
            {
                report.Line("ERR no prompt is open");
            }
        });

        report.Register(methods, "greeter/cancel", "cancel", flow.Cancel);

        report.Register(methods, "greeter/dismiss", "dismiss", flow.Dismiss);

        report.Register(methods, "greeter/session", "session {id}", (ref IpcParams p, IpcReply _) =>
        {
            var id = p.GetString("id");
            if (!p.Failed && !flow.SelectSession(id, byUser: true))
            {
                report.Line($"ERR no session {id}");
            }
        });

        report.Register(methods, "greeter/layout", "layout {id}", (ref IpcParams p, IpcReply _) =>
        {
            var id = p.GetString("id");
            if (p.Failed)
            {
                return;
            }

            if (flow.Settings.Layouts.Contains(id))
            {
                flow.SelectLayout(id, byUser: true);
            }
            else
            {
                report.Line($"ERR no layout {id}");
            }
        });

        report.Register(methods, "greeter/command", "command {text...}", (ref IpcParams p, IpcReply _) =>
        {
            var text = p.GetString("text");
            if (!p.Failed)
            {
                flow.Command = text;
            }
        });

        report.Register(methods, "greeter/state", "state", () =>
        {
            report.Line($"STATE phase={flow.Phase.ToString().ToLowerInvariant()} user={flow.CurrentUser ?? "none"} prompt=\"{flow.PromptText}\" session={flow.SelectedSession ?? "none"} layout={flow.SelectedLayout ?? "system"}");
            report.Line($"STATE path={flow.Memory.Path} last_user={flow.Memory.LastUser ?? "none"}");
            foreach (var user in flow.Users)
            {
                if (flow.Memory.SessionOf(user.Name) is { } session)
                {
                    report.Line($"STATE user={user.Name} session={session} layout={flow.Memory.LayoutOf(user.Name) ?? "none"}");
                }
            }
        });
    }
}
