using System.CommandLine;
using Basin.Ipc;

namespace Basin.Cli;

public static class IpcCli
{
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
