namespace Basin.Ipc;

internal sealed record IpcLineForm(string Method, IpcLinePattern Pattern, IpcLineFormatter? Reply);
