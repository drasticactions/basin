using Basin.Ipc;

namespace Basin.SoftKeyboard.Ipc;

/// <summary>
/// Registers the IPC methods that drive a <see cref="SoftKeyboardSession"/> by hand: show and hide, tap, type, hold, long-press choice, size and layout.
/// </summary>
public static class SoftKeyboardSessionIpc
{
    /// <summary>
    /// Registers the seven keyboard methods under the namespace in <paramref name="hooks"/>. Taps and holds go through the session's typer, so they use the real engine path.
    /// </summary>
    /// <param name="methods">The registry that receives the methods.</param>
    /// <param name="report">The line report that registers the methods and prints the LONGPRESS line.</param>
    /// <param name="session">The keyboard session whose engine and typer the methods drive.</param>
    /// <param name="hooks">The shell callbacks and the method namespace.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static void Register(IpcMethodRegistry methods, IpcLineReport report, SoftKeyboardSession session, SoftKeyboardIpcHooks hooks)
    {
        ArgumentNullException.ThrowIfNull(methods);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(hooks);
        var ns = hooks.Namespace;
        var engine = session.Engine;
        var typer = session.Typer;

        report.Register(methods, ns + "/keyboard", "keyboard [{state}]",
            Info("Raise, hide or toggle the on-screen keyboard by hand, or print its KEYBOARD line.",
                """{"type":"object","properties":{"state":{"type":"string","enum":["show","hide","toggle"]}}}"""),
            (ref IpcParams p, IpcReply _) =>
            {
                var state = p.TryGetString("state", out var given) ? given : "";
                switch (state)
                {
                    case "show":
                        hooks.Show();
                        break;
                    case "hide":
                        hooks.Hide("command");
                        break;
                    case "toggle":
                        hooks.Toggle();
                        break;
                    default:
                        hooks.Report();
                        break;
                }
            });

        report.Register(methods, ns + "/keyboard-tap", "keyboard-tap {key}",
            Info("Press and release one keyboard key by its cap: a letter, shift, space, backspace, return, sym, hide, lang, tab, .com."),
            (ref IpcParams p, IpcReply reply) =>
            {
                var key = p.GetString("key");
                if (!p.Failed && !(hooks.IsShown() && typer.TapCell(typer.Find(key), 100)))
                {
                    reply.Error(IpcErrorCodes.NotFound, hooks.IsShown() ? $"no key {key}" : "the keyboard is not shown");
                }
            });

        report.Register(methods, ns + "/keyboard-type", "keyboard-type {text...}",
            Info("Tap each character's key in turn through the keyboard engine, with Shift, the symbol page and long-press lists as needed."),
            (ref IpcParams p, IpcReply reply) =>
            {
                var text = p.GetString("text");
                if (p.Failed)
                {
                    return;
                }

                if (!hooks.IsShown())
                {
                    reply.Error(IpcErrorCodes.InvalidParams, "the keyboard is not shown");
                    return;
                }

                foreach (var c in text)
                {
                    if (!typer.TypeCharacter(c))
                    {
                        reply.Error(IpcErrorCodes.NotFound, $"no key types '{c}'");
                        return;
                    }
                }
            });

        report.Register(methods, ns + "/keyboard-hold", "keyboard-hold {key} {ms:int}",
            Info("Press a keyboard key, hold it for ms milliseconds of keyboard time, and release it. A long press opens the key's list."),
            (ref IpcParams p, IpcReply reply) =>
            {
                var key = p.GetString("key");
                var ms = (int)p.GetInt("ms");
                if (p.Failed)
                {
                    return;
                }

                var cell = typer.Find(key);
                if (cell < 0 || !hooks.IsShown())
                {
                    reply.Error(IpcErrorCodes.NotFound, $"no key {key}");
                    return;
                }

                typer.HoldCell(cell, ms);
                if (engine.IsLongPressOpen)
                {
                    report.Line($"LONGPRESS {string.Join(',', engine.LongPressEntries!.Select(static e => e.IsText ? e.Text : e.Label ?? e.Name))}");
                }
            });

        report.Register(methods, ns + "/keyboard-choose", "keyboard-choose {index:int}",
            Info("Choose an entry of the open long-press list, counted from 0."),
            (ref IpcParams p, IpcReply reply) =>
            {
                var index = (int)p.GetInt("index");
                if (p.Failed)
                {
                    return;
                }

                if (!engine.IsLongPressOpen || index < 0 || index >= engine.LongPressEntries!.Count)
                {
                    reply.Error(IpcErrorCodes.InvalidParams, "no such long-press entry");
                    return;
                }

                engine.ChooseLongPress(index, typer.Step(100));
            });

        report.Register(methods, ns + "/keyboard-size", "keyboard-size {size}",
            Info("Set the keyboard size as the Hide key's list does: xs, s, m or l.",
                """{"type":"object","properties":{"size":{"type":"string","enum":["xs","s","m","l"]}},"required":["size"]}"""),
            (ref IpcParams p, IpcReply _) =>
            {
                var size = p.GetString("size");
                if (!p.Failed)
                {
                    session.ChooseSize(KeyboardSizes.Parse(size, session.Size));
                }
            });

        report.Register(methods, ns + "/keyboard-layout", "keyboard-layout {id}",
            Info("Switch the keyboard to a layout in [keyboard] layouts, by id."),
            (ref IpcParams p, IpcReply reply) =>
            {
                var id = p.GetString("id");
                if (!p.Failed && !session.SelectLayout(id))
                {
                    reply.Error(IpcErrorCodes.NotFound, $"no layout {id} in [keyboard] layouts");
                }
            });
    }

    private static IpcMethodInfo Info(string description, string? schema = null) => new(description, schema, IpcMethodTraits.None);
}
