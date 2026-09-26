using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Basin.Ipc;

public sealed partial class BasinIpcClient
{
    private static IpcJsonContext Json => IpcJsonContext.Default;

    public Task<TResult> CallAsync<TParams, TResult>(
        string method,
        TParams parameters,
        JsonTypeInfo<TParams> paramsInfo,
        JsonTypeInfo<TResult> resultInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paramsInfo);
        ArgumentNullException.ThrowIfNull(resultInfo);
        return CallAsync(method, writer => JsonSerializer.Serialize(writer, parameters, paramsInfo), result => result.Read(resultInfo), cancellationToken);
    }

    public Task<TResult> CallAsync<TResult>(string method, JsonTypeInfo<TResult> resultInfo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resultInfo);
        return CallAsync(method, null, result => result.Read(resultInfo), cancellationToken);
    }

    public Task<IpcVersion> VersionAsync(CancellationToken cancellationToken = default) =>
        CallAsync(IpcMethodNames.Version, Json.IpcVersion, cancellationToken);

    public async Task<IReadOnlyList<string>> MethodsAsync(CancellationToken cancellationToken = default) =>
        (await CallAsync(IpcMethodNames.Methods, Json.IpcMethodList, cancellationToken).ConfigureAwait(false)).Methods.Items();

    public async Task<IReadOnlyList<IpcMethodDetail>> MethodsDetailAsync(CancellationToken cancellationToken = default) =>
        (await CallAsync(IpcMethodNames.Methods, new IpcMethodsParams(true), Json.IpcMethodsParams, Json.IpcMethodDetailList, cancellationToken)
            .ConfigureAwait(false)).Methods.Items();

    public async Task<IReadOnlyList<string>> EventsAsync(CancellationToken cancellationToken = default) =>
        (await CallAsync(IpcMethodNames.Events, Json.IpcEventList, cancellationToken).ConfigureAwait(false)).Events.Items();

    public Task<IpcSessionDescription> DescribeSessionAsync(CancellationToken cancellationToken = default) =>
        CallAsync(IpcMethodNames.SessionDescribe, Json.IpcSessionDescription, cancellationToken);

    public Task QuitAsync(CancellationToken cancellationToken = default) =>
        CallAsync(IpcMethodNames.SessionQuit, Json.IpcEmpty, cancellationToken);

    public async Task<IReadOnlyList<IpcOutput>> ListOutputsAsync(CancellationToken cancellationToken = default) =>
        (await CallAsync(IpcMethodNames.OutputsList, Json.IpcOutputList, cancellationToken).ConfigureAwait(false)).Outputs.Items();

    public async Task<bool> TestOutputsAsync(IReadOnlyList<IpcOutputChange> changes, CancellationToken cancellationToken = default) =>
        (await CallAsync(IpcMethodNames.OutputsTest, new IpcOutputsParams(changes), Json.IpcOutputsParams, Json.IpcOutputTestResult, cancellationToken)
            .ConfigureAwait(false)).Ok;

    public Task ApplyOutputsAsync(IReadOnlyList<IpcOutputChange> changes, CancellationToken cancellationToken = default) =>
        CallAsync(IpcMethodNames.OutputsApply, new IpcOutputsParams(changes), Json.IpcOutputsParams, Json.IpcEmpty, cancellationToken);

    public Task SetOutputPowerAsync(string output, bool on, CancellationToken cancellationToken = default) =>
        CallAsync(IpcMethodNames.OutputsPower, new IpcPowerParams(output, on), Json.IpcPowerParams, Json.IpcEmpty, cancellationToken);

    public async Task<IReadOnlyList<IpcWindow>> ListWindowsAsync(CancellationToken cancellationToken = default) =>
        (await CallAsync(IpcMethodNames.WindowsList, Json.IpcWindowList, cancellationToken).ConfigureAwait(false)).Windows.Items();

    public Task<IpcWindow> GetWindowAsync(ulong id, CancellationToken cancellationToken = default) =>
        CallAsync(IpcMethodNames.WindowsGet, new IpcIdParams(id), Json.IpcIdParams, Json.IpcWindow, cancellationToken);

    public async Task<IReadOnlyList<ulong>> StackAsync(CancellationToken cancellationToken = default) =>
        (await CallAsync(IpcMethodNames.WindowsStack, Json.IpcStack, cancellationToken).ConfigureAwait(false)).Ids.Items();

    public Task ActivateAsync(ulong id, CancellationToken cancellationToken = default) => Id(IpcMethodNames.WindowsActivate, id, cancellationToken);

    public Task CloseAsync(ulong id, CancellationToken cancellationToken = default) => Id(IpcMethodNames.WindowsClose, id, cancellationToken);

    public Task SetStateAsync(
        ulong id,
        bool? maximized = null,
        bool? minimized = null,
        bool? fullscreen = null,
        bool? noBorder = null,
        CancellationToken cancellationToken = default) =>
        CallAsync(
            IpcMethodNames.WindowsSetState,
            new IpcSetStateParams(id, maximized, minimized, fullscreen, noBorder),
            Json.IpcSetStateParams,
            Json.IpcEmpty,
            cancellationToken);

    public Task MoveAsync(ulong id, int x, int y, CancellationToken cancellationToken = default) =>
        CallAsync(IpcMethodNames.WindowsMove, new IpcMoveParams(id, x, y), Json.IpcMoveParams, Json.IpcEmpty, cancellationToken);

    public Task ResizeAsync(ulong id, int width, int height, CancellationToken cancellationToken = default) =>
        CallAsync(IpcMethodNames.WindowsResize, new IpcResizeParams(id, width, height), Json.IpcResizeParams, Json.IpcEmpty, cancellationToken);

    public Task SendToOutputAsync(ulong id, string output, CancellationToken cancellationToken = default) =>
        CallAsync(IpcMethodNames.WindowsSendToOutput, new IpcSendToOutputParams(id, output), Json.IpcSendToOutputParams, Json.IpcEmpty, cancellationToken);

    public Task SendToWorkspaceAsync(ulong id, ulong workspace, CancellationToken cancellationToken = default) =>
        CallAsync(
            IpcMethodNames.WindowsSendToWorkspace,
            new IpcSendToWorkspaceParams(id, workspace),
            Json.IpcSendToWorkspaceParams,
            Json.IpcEmpty,
            cancellationToken);

    public Task<IpcWindow> WaitForWindowAsync(
        string? appId = null, string? title = null, int timeoutMs = 5000, CancellationToken cancellationToken = default) =>
        CallAsync(IpcMethodNames.WindowsWait, new IpcWaitParams(appId, title, timeoutMs), Json.IpcWaitParams, Json.IpcWindow, cancellationToken);

    public Task<IpcWaitIdleResult> WaitIdleAsync(
        ulong? id = null, long? quietMs = null, long? ignoreBelow = null, long? timeoutMs = null, CancellationToken cancellationToken = default) =>
        CallAsync(
            IpcMethodNames.WindowsWaitIdle,
            new IpcWaitIdleParams(id, quietMs, ignoreBelow, timeoutMs),
            Json.IpcWaitIdleParams,
            Json.IpcWaitIdleResult,
            cancellationToken);

    public async Task<IReadOnlyList<IpcWorkspaceGroup>> ListWorkspacesAsync(CancellationToken cancellationToken = default) =>
        (await CallAsync(IpcMethodNames.WorkspacesList, Json.IpcWorkspaceList, cancellationToken).ConfigureAwait(false)).Groups.Items();

    public Task ActivateWorkspaceAsync(ulong id, CancellationToken cancellationToken = default) =>
        Id(IpcMethodNames.WorkspacesActivate, id, cancellationToken);

    public Task DeactivateWorkspaceAsync(ulong id, CancellationToken cancellationToken = default) =>
        Id(IpcMethodNames.WorkspacesDeactivate, id, cancellationToken);

    public Task RemoveWorkspaceAsync(ulong id, CancellationToken cancellationToken = default) =>
        Id(IpcMethodNames.WorkspacesRemove, id, cancellationToken);

    public Task CreateWorkspaceAsync(ulong group, string name, CancellationToken cancellationToken = default) =>
        CallAsync(IpcMethodNames.WorkspacesCreate, new IpcWorkspaceCreateParams(group, name), Json.IpcWorkspaceCreateParams, Json.IpcEmpty, cancellationToken);

    public Task MoveWorkspaceAsync(ulong id, ulong group, CancellationToken cancellationToken = default) =>
        CallAsync(IpcMethodNames.WorkspacesMove, new IpcWorkspaceMoveParams(id, group), Json.IpcWorkspaceMoveParams, Json.IpcEmpty, cancellationToken);

    public async Task<(long IdleMillis, bool Inhibited)> IdleStatusAsync(CancellationToken cancellationToken = default)
    {
        var status = await CallAsync(IpcMethodNames.IdleStatus, Json.IpcIdleStatus, cancellationToken).ConfigureAwait(false);
        return (status.IdleMs, status.Inhibited);
    }

    public Task NotifyActivityAsync(CancellationToken cancellationToken = default) =>
        CallAsync(IpcMethodNames.IdleActivity, Json.IpcEmpty, cancellationToken);

    public async Task<string> InhibitIdleAsync(CancellationToken cancellationToken = default) =>
        (await CallAsync(IpcMethodNames.IdleInhibit, Json.IpcIdleToken, cancellationToken).ConfigureAwait(false)).Token;

    public Task UninhibitIdleAsync(string token, CancellationToken cancellationToken = default) =>
        CallAsync(IpcMethodNames.IdleUninhibit, new IpcTokenParams(token), Json.IpcTokenParams, Json.IpcEmpty, cancellationToken);

    public async Task<bool> IsLockedAsync(CancellationToken cancellationToken = default) =>
        (await CallAsync(IpcMethodNames.LockStatus, Json.IpcLockStatus, cancellationToken).ConfigureAwait(false)).Locked;

    public async Task<string?> KeymapTextAsync(CancellationToken cancellationToken = default) =>
        (await CallAsync(IpcMethodNames.KeyboardKeymap, new IpcKeymapParams(text: true), Json.IpcKeymapParams, Json.IpcKeymapInfo, cancellationToken)
            .ConfigureAwait(false)).Text;

    public Task PointerMoveAsync(double x, double y, bool seat = false, CancellationToken cancellationToken = default) =>
        CallAsync(
            seat ? IpcMethodNames.SeatPointerMove : IpcMethodNames.InputPointerMove,
            new IpcPointerMoveParams(x, y),
            Json.IpcPointerMoveParams,
            Json.IpcEmpty,
            cancellationToken);

    public Task PointerMoveInWindowAsync(
        ulong window, double x, double y, bool raise = false, bool seat = false, CancellationToken cancellationToken = default) =>
        CallAsync(
            seat ? IpcMethodNames.SeatPointerMove : IpcMethodNames.InputPointerMove,
            new IpcPointerMoveParams(x, y) { Window = window, Raise = raise ? true : null },
            Json.IpcPointerMoveParams,
            Json.IpcEmpty,
            cancellationToken);

    public Task PointerButtonInWindowAsync(
        ulong window,
        double x,
        double y,
        uint button,
        bool? pressed = null,
        bool raise = false,
        bool seat = false,
        CancellationToken cancellationToken = default) =>
        CallAsync(
            seat ? IpcMethodNames.SeatPointerButton : IpcMethodNames.InputPointerButton,
            new IpcPointerButtonParams(new IpcButton(button), pressed) { Window = window, X = x, Y = y, Raise = raise ? true : null },
            Json.IpcPointerButtonParams,
            Json.IpcEmpty,
            cancellationToken);

    public Task PointerButtonAsync(uint button, bool? pressed = null, bool seat = false, CancellationToken cancellationToken = default) =>
        CallAsync(
            seat ? IpcMethodNames.SeatPointerButton : IpcMethodNames.InputPointerButton,
            new IpcPointerButtonParams(new IpcButton(button), pressed),
            Json.IpcPointerButtonParams,
            Json.IpcEmpty,
            cancellationToken);

    public Task AxisAsync(double value, bool horizontal = false, bool seat = false, CancellationToken cancellationToken = default) =>
        CallAsync(
            seat ? IpcMethodNames.SeatAxis : IpcMethodNames.InputAxis,
            new IpcAxisParams(value, horizontal ? "horizontal" : "vertical"),
            Json.IpcAxisParams,
            Json.IpcEmpty,
            cancellationToken);

    public Task KeyAsync(uint code, bool? pressed = null, bool seat = false, CancellationToken cancellationToken = default) =>
        CallAsync(
            seat ? IpcMethodNames.SeatKey : IpcMethodNames.InputKey,
            new IpcKeyParams((int)code, pressed),
            Json.IpcKeyParams,
            Json.IpcEmpty,
            cancellationToken);

    public Task ChordAsync(string chord, bool seat = false, CancellationToken cancellationToken = default) =>
        CallAsync(
            seat ? IpcMethodNames.SeatChord : IpcMethodNames.InputChord,
            new IpcChordParams(chord),
            Json.IpcChordParams,
            Json.IpcChordResult,
            cancellationToken);

    public Task TextAsync(string text, bool seat = false, CancellationToken cancellationToken = default) =>
        CallAsync(seat ? IpcMethodNames.SeatText : IpcMethodNames.InputText, new IpcTextParams(text), Json.IpcTextParams, Json.IpcEmpty, cancellationToken);

    public async Task<string> TextAsync(string text, string via, bool seat = false, CancellationToken cancellationToken = default) =>
        (await CallAsync(
            seat ? IpcMethodNames.SeatText : IpcMethodNames.InputText,
            new IpcTextParams(text) { Via = via },
            Json.IpcTextParams,
            Json.IpcTextResult,
            cancellationToken).ConfigureAwait(false)).Via;

    public Task WriteClipboardAsync(string text, bool primary = false, CancellationToken cancellationToken = default) =>
        CallAsync(
            IpcMethodNames.ClipboardWrite,
            new IpcClipboardWriteParams(text, primary ? "primary" : null),
            Json.IpcClipboardWriteParams,
            Json.IpcEmpty,
            cancellationToken);

    public Task TouchAsync(
        string kind, int id = 0, double x = 0, double y = 0, bool seat = false, CancellationToken cancellationToken = default) =>
        CallAsync(
            seat ? IpcMethodNames.SeatTouch : IpcMethodNames.InputTouch,
            new IpcTouchParams(kind, id, x, y),
            Json.IpcTouchParams,
            Json.IpcEmpty,
            cancellationToken);

    public Task<IpcCapture> CaptureOutputAsync(
        string? output,
        IpcCaptureTarget target,
        bool cursor = false,
        double scale = 1,
        int? maxDimension = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        return CaptureAsync(
            IpcMethodNames.CaptureOutput,
            new IpcCaptureOutputParams(target.Wire, output, cursor, maxDimension is null ? scale : null, maxDimension),
            Json.IpcCaptureOutputParams,
            cancellationToken);
    }

    public Task<IpcCapture> CaptureWindowAsync(
        ulong id,
        IpcCaptureTarget target,
        bool clientOnly = false,
        double scale = 1,
        int? maxDimension = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        return CaptureAsync(
            IpcMethodNames.CaptureWindow,
            new IpcCaptureWindowParams(id, target.Wire, clientOnly, null, maxDimension is null ? scale : null, maxDimension),
            Json.IpcCaptureWindowParams,
            cancellationToken);
    }

    public Task<IpcCapture> CaptureRegionAsync(
        IpcBox region,
        IpcCaptureTarget target,
        double scale = 1,
        int? maxDimension = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        return CaptureAsync(
            IpcMethodNames.CaptureRegion,
            new IpcCaptureRegionParams(region.X, region.Y, region.Width, region.Height, target.Wire, null, maxDimension is null ? scale : null, maxDimension),
            Json.IpcCaptureRegionParams,
            cancellationToken);
    }

    public Task<IpcClipboard> ReadClipboardAsync(
        bool primary = false, string? mime = null, int? timeoutMs = null, CancellationToken cancellationToken = default) =>
        CallAsync(
            IpcMethodNames.ClipboardRead,
            new IpcClipboardParams(primary ? "primary" : "clipboard", mime, timeoutMs),
            Json.IpcClipboardParams,
            Json.IpcClipboard,
            cancellationToken);

    public Task<IpcSpawnResult> LaunchAsync(IpcSpawnParams launch, CancellationToken cancellationToken = default) =>
        CallAsync(IpcMethodNames.ProcessSpawn, launch, Json.IpcSpawnParams, Json.IpcSpawnResult, cancellationToken);

    public async Task<IReadOnlyList<IpcProcess>> ListProcessesAsync(CancellationToken cancellationToken = default) =>
        (await CallAsync(IpcMethodNames.ProcessList, Json.IpcProcessList, cancellationToken).ConfigureAwait(false)).Processes.Items();

    public Task KillProcessAsync(long launchId, int? graceMs = null, CancellationToken cancellationToken = default) =>
        CallAsync(
            IpcMethodNames.ProcessKill,
            new IpcProcessKillParams(launchId, graceMs),
            Json.IpcProcessKillParams,
            Json.IpcEmpty,
            cancellationToken);

    public Task<IpcProcessLog> ProcessLogAsync(long launchId, int? lines = null, CancellationToken cancellationToken = default) =>
        CallAsync(
            IpcMethodNames.ProcessLog,
            new IpcProcessLogParams(launchId, lines),
            Json.IpcProcessLogParams,
            Json.IpcProcessLog,
            cancellationToken);

    public async Task<int> SpawnAsync(
        IReadOnlyList<string> argv,
        IReadOnlyDictionary<string, string>? env = null,
        string? cwd = null,
        CancellationToken cancellationToken = default) =>
        (await CallAsync(IpcMethodNames.ProcessSpawn, new IpcSpawnParams(argv, env, cwd), Json.IpcSpawnParams, Json.IpcSpawnResult, cancellationToken)
            .ConfigureAwait(false)).Pid;

    private async Task<IpcCapture> CaptureAsync<TParams>(
        string method, TParams parameters, JsonTypeInfo<TParams> info, CancellationToken cancellationToken)
    {
        var result = await CallAsync(method, writer => JsonSerializer.Serialize(writer, parameters, info), cancellationToken).ConfigureAwait(false);
        var reply = result.Read(Json.IpcCaptureResult);
        var fd = reply.Fd is { } index ? result.Fd(index) : null;
        foreach (var handle in result.Fds)
        {
            if (!ReferenceEquals(handle, fd))
            {
                handle.Dispose();
            }
        }

        return new IpcCapture(reply.Width, reply.Height, reply.Path, reply.Png, fd, reply.Format, reply.Stride ?? 0);
    }

    private Task Id(string method, ulong id, CancellationToken cancellationToken) =>
        CallAsync(method, new IpcIdParams(id), Json.IpcIdParams, Json.IpcEmpty, cancellationToken);
}
