using System.CommandLine;
using Basin.Ipc;

namespace Basin.Cli;

/// <summary>
/// Adds the <c>--ipc</c> option to a program and reads it.
/// </summary>
public static class IpcCli
{
    /// <summary>
    /// Adds <c>--ipc</c> to the command once. A second call returns the option that the first call added.
    /// </summary>
    /// <param name="command">The command to add the option to.</param>
    /// <returns>The <c>--ipc</c> option.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> is <see langword="null"/>.</exception>
    public static Option<string> AddOption(BasinCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.IpcOption is { } existing)
        {
            return existing;
        }

        var option = command.Add(CommonOptions.Ipc(), report: false);
        command.IpcOption = option;
        command.AddReport(result => BasinCommand.Report("ipc", Describe(result.GetValue(option))));
        return option;
    }

    /// <summary>
    /// Reads the <c>--ipc</c> value. When the option was not given, an absolute path in the inherited IPC path variable wins over the default path. A command without the option always listens.
    /// </summary>
    /// <param name="command">The command that carries the option.</param>
    /// <param name="result">The parse result.</param>
    /// <returns><see cref="IpcChoice.Off"/> for <c>false</c>, <c>off</c>, <c>no</c> or <c>0</c>, otherwise a listening choice with an absolute path or the default path.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> or <paramref name="result"/> is <see langword="null"/>.</exception>
    public static IpcChoice Read(BasinCommand command, System.CommandLine.ParseResult result)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(result);
        var value = command.IpcOption is { } option ? result.GetValue(option) : "true";
        if (IsOff(value))
        {
            return IpcChoice.Off;
        }

        var given = command.IpcOption is { } flag && result.GetResult(flag) is { Implicit: false };
        if (!given && Environment.GetEnvironmentVariable(IpcProtocol.PathVariable) is { Length: > 0 } inherited && Path.IsPathRooted(inherited))
        {
            return new IpcChoice(true, inherited);
        }

        return new IpcChoice(true, IsOn(value) ? null : Path.GetFullPath(value!));
    }

    /// <summary>
    /// Reads <c>--ipc</c> and creates a control server from it. The caller must start and dispose the server.
    /// </summary>
    /// <param name="command">The command that carries the option.</param>
    /// <param name="result">The parse result.</param>
    /// <param name="loop">The compositor loop that the server runs on.</param>
    /// <param name="services">The services that the IPC methods read.</param>
    /// <param name="socketName">The compositor's Wayland socket name, or <see langword="null"/> to keep the one in <paramref name="info"/>.</param>
    /// <param name="info">The session information that the server reports.</param>
    /// <param name="lineFront">Whether to create the server even when <c>--ipc</c> is off, so that stdin commands can use it.</param>
    /// <returns>The server, not started, or <see langword="null"/> when <c>--ipc</c> is off and <paramref name="lineFront"/> is <see langword="false"/>.</returns>
    public static IpcServer? Attach(
        BasinCommand command,
        System.CommandLine.ParseResult result,
        ICompositorEventLoop loop,
        BasinServices services,
        string? socketName,
        IpcSessionInfo info,
        bool lineFront = false)
    {
        var choice = Read(command, result);
        return lineFront
            ? choice.Attach(loop, services, socketName, info)
            : choice.AttachIfListening(loop, services, socketName, info);
    }

    private static bool IsOff(string? value) => value is "false" or "off" or "no" or "0";

    private static bool IsOn(string? value) => value is null or "" or "true" or "on" or "yes" or "1";

    private static string Describe(string? value) => IsOff(value) ? "off" : IsOn(value) ? "on" : value!;
}
