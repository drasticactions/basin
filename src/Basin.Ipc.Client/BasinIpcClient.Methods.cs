using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Basin.Ipc;

/// <content>
/// The typed calls of the library methods. Each one raises <see cref="IpcCallException"/> on an error reply.
/// </content>
public sealed partial class BasinIpcClient
{
    private static IpcJsonContext Json => IpcJsonContext.Default;

    /// <summary>
    /// Calls a method with a params contract and reads a result contract.
    /// </summary>
    /// <typeparam name="TParams">The params contract type.</typeparam>
    /// <typeparam name="TResult">The result contract type.</typeparam>
    /// <param name="method">The method name.</param>
    /// <param name="parameters">The params.</param>
    /// <param name="paramsInfo">The source-generated metadata of <typeparamref name="TParams"/>.</param>
    /// <param name="resultInfo">The source-generated metadata of <typeparamref name="TResult"/>.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>The result. Descriptors that came with it are closed.</returns>
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

    /// <summary>
    /// Calls a method with no params and reads a result contract.
    /// </summary>
    /// <typeparam name="TResult">The result contract type.</typeparam>
    /// <param name="method">The method name.</param>
    /// <param name="resultInfo">The source-generated metadata of <typeparamref name="TResult"/>.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>The result. Descriptors that came with it are closed.</returns>
    public Task<TResult> CallAsync<TResult>(string method, JsonTypeInfo<TResult> resultInfo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resultInfo);
        return CallAsync(method, null, result => result.Read(resultInfo), cancellationToken);
    }

    /// <summary>
    /// Calls <c>ipc/version</c>.
    /// </summary>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>The protocol version, the compositor name and the basin version.</returns>
    public Task<IpcVersion> VersionAsync(CancellationToken cancellationToken = default) =>
        CallAsync(IpcMethodNames.Version, Json.IpcVersion, cancellationToken);

    /// <summary>
    /// Calls <c>ipc/methods</c>.
    /// </summary>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>The names of the methods that this compositor registered.</returns>
    public async Task<IReadOnlyList<string>> MethodsAsync(CancellationToken cancellationToken = default) =>
        (await CallAsync(IpcMethodNames.Methods, Json.IpcMethodList, cancellationToken).ConfigureAwait(false)).Methods.Items();

    /// <summary>
    /// Calls <c>ipc/methods</c> with <c>detail</c>.
    /// </summary>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>Each method with its description, schema, traits and first line form.</returns>
    public async Task<IReadOnlyList<IpcMethodDetail>> MethodsDetailAsync(CancellationToken cancellationToken = default) =>
        (await CallAsync(IpcMethodNames.Methods, new IpcMethodsParams(true), Json.IpcMethodsParams, Json.IpcMethodDetailList, cancellationToken)
            .ConfigureAwait(false)).Methods.Items();

    /// <summary>
    /// Calls <c>ipc/events</c>.
    /// </summary>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>The names of the events that a client can subscribe to.</returns>
    public async Task<IReadOnlyList<string>> EventsAsync(CancellationToken cancellationToken = default) =>
        (await CallAsync(IpcMethodNames.Events, Json.IpcEventList, cancellationToken).ConfigureAwait(false)).Events.Items();

    /// <summary>
    /// Calls <c>session/describe</c>.
    /// </summary>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>The compositor, backend, renderer, sockets, output count and process id.</returns>
    public Task<IpcSessionDescription> DescribeSessionAsync(CancellationToken cancellationToken = default) =>
        CallAsync(IpcMethodNames.SessionDescribe, Json.IpcSessionDescription, cancellationToken);

    /// <summary>
    /// Calls <c>session/quit</c>, which stops the compositor. A compositor with no stop action answers <c>unknown_method</c>.
    /// </summary>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>A task that completes when the compositor accepts the request.</returns>
    public Task QuitAsync(CancellationToken cancellationToken = default) =>
        CallAsync(IpcMethodNames.SessionQuit, Json.IpcEmpty, cancellationToken);

    /// <summary>
    /// Calls <c>session/settle</c>, which waits until every output presented what it was asked to draw, every window answered its newest configure and no animation runs.
    /// </summary>
    /// <param name="timeoutMs">The longest wait in milliseconds, or <see langword="null"/> for the default of 30000.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>The count of frames presented on all outputs.</returns>
    public async Task<long> SettleAsync(long? timeoutMs = null, CancellationToken cancellationToken = default) =>
        (await CallAsync(IpcMethodNames.SessionSettle, new IpcSettleParams(timeoutMs), Json.IpcSettleParams, Json.IpcSettleResult, cancellationToken)
            .ConfigureAwait(false)).Presented;

    /// <summary>
    /// Calls <c>outputs/list</c>.
    /// </summary>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>Every output with its mode, geometry and state.</returns>
    public async Task<IReadOnlyList<IpcOutput>> ListOutputsAsync(CancellationToken cancellationToken = default) =>
        (await CallAsync(IpcMethodNames.OutputsList, Json.IpcOutputList, cancellationToken).ConfigureAwait(false)).Outputs.Items();

    /// <summary>
    /// Calls <c>outputs/test</c>, which checks a configuration without applying it.
    /// </summary>
    /// <param name="changes">One entry per output that changes.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns><see langword="true"/> when the backend accepts the configuration.</returns>
    public async Task<bool> TestOutputsAsync(IReadOnlyList<IpcOutputChange> changes, CancellationToken cancellationToken = default) =>
        (await CallAsync(IpcMethodNames.OutputsTest, new IpcOutputsParams(changes), Json.IpcOutputsParams, Json.IpcOutputTestResult, cancellationToken)
            .ConfigureAwait(false)).Ok;

    /// <summary>
    /// Calls <c>outputs/apply</c>, which applies a configuration.
    /// </summary>
    /// <param name="changes">One entry per output that changes. An entry names only what it changes.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>A task that completes when the configuration is applied.</returns>
    public Task ApplyOutputsAsync(IReadOnlyList<IpcOutputChange> changes, CancellationToken cancellationToken = default) =>
        CallAsync(IpcMethodNames.OutputsApply, new IpcOutputsParams(changes), Json.IpcOutputsParams, Json.IpcEmpty, cancellationToken);

    /// <summary>
    /// Calls <c>outputs/look</c>, which applies a <c>.cube</c> 3D LUT to an output.
    /// </summary>
    /// <param name="output">The output name.</param>
    /// <param name="path">The LUT file, or <see langword="null"/> to remove the look.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>A task that completes when the look is applied.</returns>
    public Task SetOutputLookAsync(string output, string? path, CancellationToken cancellationToken = default) =>
        CallAsync(IpcMethodNames.OutputsLook, new IpcLookParams(output, path), Json.IpcLookParams, Json.IpcEmpty, cancellationToken);

    /// <summary>
    /// Calls <c>outputs/power</c>. The output keeps its place in the layout.
    /// </summary>
    /// <param name="output">The output name.</param>
    /// <param name="on"><see langword="true"/> to turn the display power on.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>A task that completes when the power state is set.</returns>
    public Task SetOutputPowerAsync(string output, bool on, CancellationToken cancellationToken = default) =>
        CallAsync(IpcMethodNames.OutputsPower, new IpcPowerParams(output, on), Json.IpcPowerParams, Json.IpcEmpty, cancellationToken);

    /// <summary>
    /// Calls <c>windows/list</c>.
    /// </summary>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>Every window.</returns>
    public async Task<IReadOnlyList<IpcWindow>> ListWindowsAsync(CancellationToken cancellationToken = default) =>
        (await CallAsync(IpcMethodNames.WindowsList, Json.IpcWindowList, cancellationToken).ConfigureAwait(false)).Windows.Items();

    /// <summary>
    /// Calls <c>windows/set-x11-density</c>, which moves an X11 program to the native or the low-density X server. The answer can take the control's grace time.
    /// </summary>
    /// <param name="id">The window id.</param>
    /// <param name="density"><c>native</c> or <c>low</c>.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>The outcome and the launch rule that was written.</returns>
    public Task<IpcX11DensityResult> SetX11DensityAsync(ulong id, string density, CancellationToken cancellationToken = default) =>
        CallAsync(
            IpcMethodNames.WindowsSetX11Density,
            new IpcSetX11DensityParams(id, density),
            Json.IpcSetX11DensityParams,
            Json.IpcX11DensityResult,
            cancellationToken);

    /// <summary>
    /// Calls <c>windows/get</c>.
    /// </summary>
    /// <param name="id">The window id.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>The window. An unknown id raises <see cref="IpcCallException"/> with <c>not_found</c>.</returns>
    public Task<IpcWindow> GetWindowAsync(ulong id, CancellationToken cancellationToken = default) =>
        CallAsync(IpcMethodNames.WindowsGet, new IpcIdParams(id), Json.IpcIdParams, Json.IpcWindow, cancellationToken);

    /// <summary>
    /// Calls <c>windows/stack</c>.
    /// </summary>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>The window ids in stacking order.</returns>
    public async Task<IReadOnlyList<ulong>> StackAsync(CancellationToken cancellationToken = default) =>
        (await CallAsync(IpcMethodNames.WindowsStack, Json.IpcStack, cancellationToken).ConfigureAwait(false)).Ids.Items();

    /// <summary>
    /// Calls <c>windows/activate</c>. A compositor that declines answers <c>refused</c>.
    /// </summary>
    /// <param name="id">The window id.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>A task that completes when the request is sent to the window.</returns>
    public Task ActivateAsync(ulong id, CancellationToken cancellationToken = default) => Id(IpcMethodNames.WindowsActivate, id, cancellationToken);

    /// <summary>
    /// Calls <c>windows/close</c>, which asks the window to close.
    /// </summary>
    /// <param name="id">The window id.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>A task that completes when the request is sent to the window.</returns>
    public Task CloseAsync(ulong id, CancellationToken cancellationToken = default) => Id(IpcMethodNames.WindowsClose, id, cancellationToken);

    /// <summary>
    /// Calls <c>windows/set-state</c>. Each state that is <see langword="null"/> stays as it is.
    /// </summary>
    /// <param name="id">The window id.</param>
    /// <param name="maximized">The maximized state to set.</param>
    /// <param name="minimized">The minimized state to set.</param>
    /// <param name="fullscreen">The fullscreen state to set.</param>
    /// <param name="noBorder">The borderless state to set.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>A task that completes when the requests are sent.</returns>
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

    /// <summary>
    /// Calls <c>windows/stats</c>. The first call starts the collection, so it can answer zeros.
    /// </summary>
    /// <param name="id">The window id.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>The commit and present rates and latencies of the last second.</returns>
    public Task<IpcFrameStats> StatsAsync(ulong id, CancellationToken cancellationToken = default) =>
        CallAsync(IpcMethodNames.WindowsStats, new IpcIdParams(id), Json.IpcIdParams, Json.IpcFrameStats, cancellationToken);

    /// <summary>
    /// Calls <c>windows/frame-limit</c>. Without a limiter the answer is <c>unavailable</c>.
    /// </summary>
    /// <param name="id">The window id.</param>
    /// <param name="fps">The limit in frames per second, from 1 to 1000, or 0 for no limit.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>A task that completes when the limit is set.</returns>
    public Task SetFrameLimitAsync(ulong id, int fps, CancellationToken cancellationToken = default) =>
        CallAsync(IpcMethodNames.WindowsFrameLimit, new IpcFrameLimitParams(id, fps), Json.IpcFrameLimitParams, Json.IpcEmpty, cancellationToken);

    /// <summary>
    /// Calls <c>windows/move</c>. A compositor that has no move policy answers <c>refused</c>.
    /// </summary>
    /// <param name="id">The window id.</param>
    /// <param name="x">The new left edge in layout coordinates.</param>
    /// <param name="y">The new top edge in layout coordinates.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>A task that completes when the compositor accepts the request.</returns>
    public Task MoveAsync(ulong id, int x, int y, CancellationToken cancellationToken = default) =>
        CallAsync(IpcMethodNames.WindowsMove, new IpcMoveParams(id, x, y), Json.IpcMoveParams, Json.IpcEmpty, cancellationToken);

    /// <summary>
    /// Calls <c>windows/resize</c>. A compositor that has no resize policy answers <c>refused</c>.
    /// </summary>
    /// <param name="id">The window id.</param>
    /// <param name="width">The new width in logical pixels.</param>
    /// <param name="height">The new height in logical pixels.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>A task that completes when the compositor accepts the request.</returns>
    public Task ResizeAsync(ulong id, int width, int height, CancellationToken cancellationToken = default) =>
        CallAsync(IpcMethodNames.WindowsResize, new IpcResizeParams(id, width, height), Json.IpcResizeParams, Json.IpcEmpty, cancellationToken);

    /// <summary>
    /// Calls <c>windows/send-to-output</c>.
    /// </summary>
    /// <param name="id">The window id.</param>
    /// <param name="output">The output name.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>A task that completes when the compositor accepts the request.</returns>
    public Task SendToOutputAsync(ulong id, string output, CancellationToken cancellationToken = default) =>
        CallAsync(IpcMethodNames.WindowsSendToOutput, new IpcSendToOutputParams(id, output), Json.IpcSendToOutputParams, Json.IpcEmpty, cancellationToken);

    /// <summary>
    /// Calls <c>windows/send-to-workspace</c>.
    /// </summary>
    /// <param name="id">The window id.</param>
    /// <param name="workspace">The workspace id.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>A task that completes when the compositor accepts the request.</returns>
    public Task SendToWorkspaceAsync(ulong id, ulong workspace, CancellationToken cancellationToken = default) =>
        CallAsync(
            IpcMethodNames.WindowsSendToWorkspace,
            new IpcSendToWorkspaceParams(id, workspace),
            Json.IpcSendToWorkspaceParams,
            Json.IpcEmpty,
            cancellationToken);

    /// <summary>
    /// Calls <c>windows/wait</c>, which answers when a matching window exists. A timeout raises <see cref="IpcCallException"/> with <c>failed</c>.
    /// </summary>
    /// <param name="appId">The exact app id, or <see langword="null"/> for any.</param>
    /// <param name="title">A substring of the title, or <see langword="null"/> for any.</param>
    /// <param name="timeoutMs">The longest wait in milliseconds.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>The first matching window.</returns>
    public Task<IpcWindow> WaitForWindowAsync(
        string? appId = null, string? title = null, int timeoutMs = 5000, CancellationToken cancellationToken = default) =>
        CallAsync(IpcMethodNames.WindowsWait, new IpcWaitParams(appId, title, timeoutMs), Json.IpcWaitParams, Json.IpcWindow, cancellationToken);

    /// <summary>
    /// Calls <c>windows/wait-idle</c>, which answers when a window has drawn nothing for a quiet time. A timeout raises <see cref="IpcCallException"/> with <c>failed</c>.
    /// </summary>
    /// <param name="id">The window id, or <see langword="null"/> for every window.</param>
    /// <param name="quietMs">The quiet time in milliseconds, or <see langword="null"/> for 300.</param>
    /// <param name="ignoreBelow">The side in logical pixels of a damage square that does not count, or <see langword="null"/> for 64.</param>
    /// <param name="timeoutMs">The longest wait in milliseconds, or <see langword="null"/> for 5000.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>The idle result.</returns>
    public Task<IpcWaitIdleResult> WaitIdleAsync(
        ulong? id = null, long? quietMs = null, long? ignoreBelow = null, long? timeoutMs = null, CancellationToken cancellationToken = default) =>
        CallAsync(
            IpcMethodNames.WindowsWaitIdle,
            new IpcWaitIdleParams(id, quietMs, ignoreBelow, timeoutMs),
            Json.IpcWaitIdleParams,
            Json.IpcWaitIdleResult,
            cancellationToken);

    /// <summary>
    /// Calls <c>workspaces/list</c>.
    /// </summary>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>The workspace groups with their outputs and workspaces.</returns>
    public async Task<IReadOnlyList<IpcWorkspaceGroup>> ListWorkspacesAsync(CancellationToken cancellationToken = default) =>
        (await CallAsync(IpcMethodNames.WorkspacesList, Json.IpcWorkspaceList, cancellationToken).ConfigureAwait(false)).Groups.Items();

    /// <summary>
    /// Calls <c>workspaces/activate</c>, which asks the compositor to show a workspace.
    /// </summary>
    /// <param name="id">The workspace id.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>A task that completes when the request is sent.</returns>
    public Task ActivateWorkspaceAsync(ulong id, CancellationToken cancellationToken = default) =>
        Id(IpcMethodNames.WorkspacesActivate, id, cancellationToken);

    /// <summary>
    /// Calls <c>workspaces/deactivate</c>, which asks the compositor to hide a workspace.
    /// </summary>
    /// <param name="id">The workspace id.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>A task that completes when the request is sent.</returns>
    public Task DeactivateWorkspaceAsync(ulong id, CancellationToken cancellationToken = default) =>
        Id(IpcMethodNames.WorkspacesDeactivate, id, cancellationToken);

    /// <summary>
    /// Calls <c>workspaces/remove</c>, which asks the compositor to remove a workspace.
    /// </summary>
    /// <param name="id">The workspace id.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>A task that completes when the request is sent.</returns>
    public Task RemoveWorkspaceAsync(ulong id, CancellationToken cancellationToken = default) =>
        Id(IpcMethodNames.WorkspacesRemove, id, cancellationToken);

    /// <summary>
    /// Calls <c>workspaces/create</c>, which asks the compositor to create a workspace in a group.
    /// </summary>
    /// <param name="group">The group id.</param>
    /// <param name="name">The name of the new workspace.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>A task that completes when the request is sent.</returns>
    public Task CreateWorkspaceAsync(ulong group, string name, CancellationToken cancellationToken = default) =>
        CallAsync(IpcMethodNames.WorkspacesCreate, new IpcWorkspaceCreateParams(group, name), Json.IpcWorkspaceCreateParams, Json.IpcEmpty, cancellationToken);

    /// <summary>
    /// Calls <c>workspaces/move</c>, which asks the compositor to move a workspace to another group.
    /// </summary>
    /// <param name="id">The workspace id.</param>
    /// <param name="group">The id of the target group.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>A task that completes when the request is sent.</returns>
    public Task MoveWorkspaceAsync(ulong id, ulong group, CancellationToken cancellationToken = default) =>
        CallAsync(IpcMethodNames.WorkspacesMove, new IpcWorkspaceMoveParams(id, group), Json.IpcWorkspaceMoveParams, Json.IpcEmpty, cancellationToken);

    /// <summary>
    /// Calls <c>idle/status</c>.
    /// </summary>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>The milliseconds since the last activity, and whether an inhibitor holds the session awake.</returns>
    public async Task<(long IdleMillis, bool Inhibited)> IdleStatusAsync(CancellationToken cancellationToken = default)
    {
        var status = await CallAsync(IpcMethodNames.IdleStatus, Json.IpcIdleStatus, cancellationToken).ConfigureAwait(false);
        return (status.IdleMs, status.Inhibited);
    }

    /// <summary>
    /// Calls <c>idle/activity</c>, which counts as user activity and resets the idle time.
    /// </summary>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>A task that completes when the activity is recorded.</returns>
    public Task NotifyActivityAsync(CancellationToken cancellationToken = default) =>
        CallAsync(IpcMethodNames.IdleActivity, Json.IpcEmpty, cancellationToken);

    /// <summary>
    /// Calls <c>idle/inhibit</c>. The inhibitor lasts until <see cref="UninhibitIdleAsync"/> or until this connection closes.
    /// </summary>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>The token that releases the inhibitor.</returns>
    public async Task<string> InhibitIdleAsync(CancellationToken cancellationToken = default) =>
        (await CallAsync(IpcMethodNames.IdleInhibit, Json.IpcIdleToken, cancellationToken).ConfigureAwait(false)).Token;

    /// <summary>
    /// Calls <c>idle/uninhibit</c>, which releases an inhibitor that this connection took.
    /// </summary>
    /// <param name="token">The token from <see cref="InhibitIdleAsync"/>.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>A task that completes when the inhibitor is released.</returns>
    public Task UninhibitIdleAsync(string token, CancellationToken cancellationToken = default) =>
        CallAsync(IpcMethodNames.IdleUninhibit, new IpcTokenParams(token), Json.IpcTokenParams, Json.IpcEmpty, cancellationToken);

    /// <summary>
    /// Calls <c>lock/status</c>.
    /// </summary>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns><see langword="true"/> when the session is locked.</returns>
    public async Task<bool> IsLockedAsync(CancellationToken cancellationToken = default) =>
        (await CallAsync(IpcMethodNames.LockStatus, Json.IpcLockStatus, cancellationToken).ConfigureAwait(false)).Locked;

    /// <summary>
    /// Calls <c>keyboard/keymap</c> with <c>text</c>.
    /// </summary>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>The XKB keymap text, or <see langword="null"/> when the compositor has none.</returns>
    public async Task<string?> KeymapTextAsync(CancellationToken cancellationToken = default) =>
        (await CallAsync(IpcMethodNames.KeyboardKeymap, new IpcKeymapParams(text: true), Json.IpcKeymapParams, Json.IpcKeymapInfo, cancellationToken)
            .ConfigureAwait(false)).Text;

    /// <summary>
    /// Calls <c>input/pointer-move</c>, or <c>seat/pointer-move</c>, to a point in layout coordinates.
    /// </summary>
    /// <param name="x">The x coordinate in the layout.</param>
    /// <param name="y">The y coordinate in the layout.</param>
    /// <param name="seat"><see langword="true"/> to go straight to the focused client, past the compositor's bindings.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>A task that completes when the motion is injected.</returns>
    public Task PointerMoveAsync(double x, double y, bool seat = false, CancellationToken cancellationToken = default) =>
        CallAsync(
            seat ? IpcMethodNames.SeatPointerMove : IpcMethodNames.InputPointerMove,
            new IpcPointerMoveParams(x, y),
            Json.IpcPointerMoveParams,
            Json.IpcEmpty,
            cancellationToken);

    /// <summary>
    /// Moves the pointer to a point in a window's client area. A point that another window covers answers <c>refused</c>.
    /// </summary>
    /// <param name="window">The window id.</param>
    /// <param name="x">The x coordinate relative to the client area.</param>
    /// <param name="y">The y coordinate relative to the client area.</param>
    /// <param name="raise"><see langword="true"/> to activate the window first when another window covers the point.</param>
    /// <param name="seat"><see langword="true"/> to go straight to the focused client, past the compositor's bindings.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>A task that completes when the motion is injected.</returns>
    public Task PointerMoveInWindowAsync(
        ulong window, double x, double y, bool raise = false, bool seat = false, CancellationToken cancellationToken = default) =>
        CallAsync(
            seat ? IpcMethodNames.SeatPointerMove : IpcMethodNames.InputPointerMove,
            new IpcPointerMoveParams(x, y) { Window = window, Raise = raise ? true : null },
            Json.IpcPointerMoveParams,
            Json.IpcEmpty,
            cancellationToken);

    /// <summary>
    /// Moves the pointer to a point in a window's client area and presses or releases a button there in the same loop turn.
    /// </summary>
    /// <param name="window">The window id.</param>
    /// <param name="x">The x coordinate relative to the client area.</param>
    /// <param name="y">The y coordinate relative to the client area.</param>
    /// <param name="button">The evdev button code, for example 272 for the left button.</param>
    /// <param name="pressed"><see langword="true"/> to press, <see langword="false"/> to release, or <see langword="null"/> to click.</param>
    /// <param name="raise"><see langword="true"/> to activate the window first when another window covers the point.</param>
    /// <param name="seat"><see langword="true"/> to go straight to the focused client, past the compositor's bindings.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>A task that completes when the button is injected.</returns>
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

    /// <summary>
    /// Calls <c>input/pointer-button</c>, or <c>seat/pointer-button</c>, at the current pointer position.
    /// </summary>
    /// <param name="button">The evdev button code, for example 272 for the left button.</param>
    /// <param name="pressed"><see langword="true"/> to press, <see langword="false"/> to release, or <see langword="null"/> to click.</param>
    /// <param name="seat"><see langword="true"/> to go straight to the focused client, past the compositor's bindings.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>A task that completes when the button is injected.</returns>
    public Task PointerButtonAsync(uint button, bool? pressed = null, bool seat = false, CancellationToken cancellationToken = default) =>
        CallAsync(
            seat ? IpcMethodNames.SeatPointerButton : IpcMethodNames.InputPointerButton,
            new IpcPointerButtonParams(new IpcButton(button), pressed),
            Json.IpcPointerButtonParams,
            Json.IpcEmpty,
            cancellationToken);

    /// <summary>
    /// Calls <c>input/axis</c>, or <c>seat/axis</c>, at the current pointer position.
    /// </summary>
    /// <param name="value">The scroll amount. A positive value scrolls down or right.</param>
    /// <param name="horizontal"><see langword="true"/> for the horizontal axis.</param>
    /// <param name="seat"><see langword="true"/> to go straight to the focused client, past the compositor's bindings.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>A task that completes when the scroll is injected.</returns>
    public Task AxisAsync(double value, bool horizontal = false, bool seat = false, CancellationToken cancellationToken = default) =>
        CallAsync(
            seat ? IpcMethodNames.SeatAxis : IpcMethodNames.InputAxis,
            new IpcAxisParams(value, horizontal ? "horizontal" : "vertical"),
            Json.IpcAxisParams,
            Json.IpcEmpty,
            cancellationToken);

    /// <summary>
    /// Calls <c>input/key</c>, or <c>seat/key</c>.
    /// </summary>
    /// <param name="code">The evdev key code, for example 30 for A.</param>
    /// <param name="pressed"><see langword="true"/> to press, <see langword="false"/> to release, or <see langword="null"/> to tap.</param>
    /// <param name="seat"><see langword="true"/> to go straight to the focused client, past the compositor's bindings.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>A task that completes when the key is injected.</returns>
    public Task KeyAsync(uint code, bool? pressed = null, bool seat = false, CancellationToken cancellationToken = default) =>
        CallAsync(
            seat ? IpcMethodNames.SeatKey : IpcMethodNames.InputKey,
            new IpcKeyParams((int)code, pressed),
            Json.IpcKeyParams,
            Json.IpcEmpty,
            cancellationToken);

    /// <summary>
    /// Calls <c>input/chord</c>, or <c>seat/chord</c>, which presses the keys in order and releases them in reverse.
    /// </summary>
    /// <param name="chord">Modifiers and one key joined by <c>+</c>, with XKB keysym names, for example <c>Super+Return</c>.</param>
    /// <param name="seat"><see langword="true"/> to go straight to the focused client, past the compositor's bindings.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>A task that completes when the chord is injected.</returns>
    public Task ChordAsync(string chord, bool seat = false, CancellationToken cancellationToken = default) =>
        CallAsync(
            seat ? IpcMethodNames.SeatChord : IpcMethodNames.InputChord,
            new IpcChordParams(chord),
            Json.IpcChordParams,
            Json.IpcChordResult,
            cancellationToken);

    /// <summary>
    /// Calls <c>input/text</c>, or <c>seat/text</c>, through the path that the compositor chooses.
    /// </summary>
    /// <param name="text">The text to type.</param>
    /// <param name="seat"><see langword="true"/> to go straight to the focused client, past the compositor's bindings.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>A task that completes when the text is sent.</returns>
    public Task TextAsync(string text, bool seat = false, CancellationToken cancellationToken = default) =>
        CallAsync(seat ? IpcMethodNames.SeatText : IpcMethodNames.InputText, new IpcTextParams(text), Json.IpcTextParams, Json.IpcEmpty, cancellationToken);

    /// <summary>
    /// Calls <c>input/text</c>, or <c>seat/text</c>, through a forced path.
    /// </summary>
    /// <param name="text">The text to type.</param>
    /// <param name="via"><c>auto</c>, <c>keymap</c> or <c>text-input</c>.</param>
    /// <param name="seat"><see langword="true"/> to go straight to the focused client, past the compositor's bindings.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>The path taken: <c>keymap</c> or <c>text-input</c>.</returns>
    public async Task<string> TextAsync(string text, string via, bool seat = false, CancellationToken cancellationToken = default) =>
        (await CallAsync(
            seat ? IpcMethodNames.SeatText : IpcMethodNames.InputText,
            new IpcTextParams(text) { Via = via },
            Json.IpcTextParams,
            Json.IpcTextResult,
            cancellationToken).ConfigureAwait(false)).Via;

    /// <summary>
    /// Calls <c>clipboard/write</c>. The text stays until another owner replaces it, also after this connection closes.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="primary"><see langword="true"/> for the primary selection instead of the clipboard.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>A task that completes when the selection is set.</returns>
    public Task WriteClipboardAsync(string text, bool primary = false, CancellationToken cancellationToken = default) =>
        CallAsync(
            IpcMethodNames.ClipboardWrite,
            new IpcClipboardWriteParams(text, primary ? "primary" : null),
            Json.IpcClipboardWriteParams,
            Json.IpcEmpty,
            cancellationToken);

    /// <summary>
    /// Calls <c>input/touch</c>, or <c>seat/touch</c>, with the time of arrival as the event time. Send <c>frame</c> after a group of events.
    /// </summary>
    /// <param name="kind"><c>down</c>, <c>motion</c>, <c>up</c>, <c>frame</c> or <c>cancel</c>.</param>
    /// <param name="id">The touch point id.</param>
    /// <param name="x">The x coordinate in the layout, for <c>down</c> and <c>motion</c>.</param>
    /// <param name="y">The y coordinate in the layout, for <c>down</c> and <c>motion</c>.</param>
    /// <param name="seat"><see langword="true"/> to go straight to the focused client, past the compositor's bindings.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>A task that completes when the event is injected.</returns>
    public Task TouchAsync(
        string kind, int id = 0, double x = 0, double y = 0, bool seat = false, CancellationToken cancellationToken = default) =>
        CallAsync(
            seat ? IpcMethodNames.SeatTouch : IpcMethodNames.InputTouch,
            new IpcTouchParams(kind, id, x, y),
            Json.IpcTouchParams,
            Json.IpcEmpty,
            cancellationToken);

    /// <summary>
    /// Calls <c>capture/output</c>.
    /// </summary>
    /// <param name="output">The output name, or <see langword="null"/> for the output under the pointer.</param>
    /// <param name="target">Where the capture goes.</param>
    /// <param name="cursor"><see langword="true"/> to draw the cursor into the capture.</param>
    /// <param name="scale">The downsampling factor. It is not sent when <paramref name="maxDimension"/> is set.</param>
    /// <param name="maxDimension">The largest size of the longer side, or <see langword="null"/>. It never scales up.</param>
    /// <param name="format"><c>png</c> or <c>png16</c>, or <see langword="null"/> for <c>png</c>.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>The capture. A descriptor in it belongs to the caller.</returns>
    public Task<IpcCapture> CaptureOutputAsync(
        string? output,
        IpcCaptureTarget target,
        bool cursor = false,
        double scale = 1,
        int? maxDimension = null,
        string? format = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        return CaptureAsync(
            IpcMethodNames.CaptureOutput,
            new IpcCaptureOutputParams(target.Wire, output, cursor, maxDimension is null ? scale : null, maxDimension, format),
            Json.IpcCaptureOutputParams,
            cancellationToken);
    }

    /// <summary>
    /// Calls <c>capture/window</c>.
    /// </summary>
    /// <param name="id">The window id.</param>
    /// <param name="target">Where the capture goes.</param>
    /// <param name="clientOnly"><see langword="true"/> to capture the client area without the frame.</param>
    /// <param name="scale">The downsampling factor. It is not sent when <paramref name="maxDimension"/> is set.</param>
    /// <param name="maxDimension">The largest size of the longer side, or <see langword="null"/>. It never scales up.</param>
    /// <param name="format"><c>png</c> or <c>png16</c>, or <see langword="null"/> for <c>png</c>.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>The capture. A descriptor in it belongs to the caller.</returns>
    public Task<IpcCapture> CaptureWindowAsync(
        ulong id,
        IpcCaptureTarget target,
        bool clientOnly = false,
        double scale = 1,
        int? maxDimension = null,
        string? format = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        return CaptureAsync(
            IpcMethodNames.CaptureWindow,
            new IpcCaptureWindowParams(id, target.Wire, clientOnly, null, maxDimension is null ? scale : null, maxDimension, format),
            Json.IpcCaptureWindowParams,
            cancellationToken);
    }

    /// <summary>
    /// Calls <c>capture/region</c>.
    /// </summary>
    /// <param name="region">The region in layout coordinates.</param>
    /// <param name="target">Where the capture goes.</param>
    /// <param name="scale">The downsampling factor. It is not sent when <paramref name="maxDimension"/> is set.</param>
    /// <param name="maxDimension">The largest size of the longer side, or <see langword="null"/>. It never scales up.</param>
    /// <param name="format"><c>png</c> or <c>png16</c>, or <see langword="null"/> for <c>png</c>.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>The capture. A descriptor in it belongs to the caller.</returns>
    public Task<IpcCapture> CaptureRegionAsync(
        IpcBox region,
        IpcCaptureTarget target,
        double scale = 1,
        int? maxDimension = null,
        string? format = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        return CaptureAsync(
            IpcMethodNames.CaptureRegion,
            new IpcCaptureRegionParams(region.X, region.Y, region.Width, region.Height, target.Wire, null, maxDimension is null ? scale : null, maxDimension, format),
            Json.IpcCaptureRegionParams,
            cancellationToken);
    }

    /// <summary>
    /// Calls <c>clipboard/read</c>, which reads the text on a selection.
    /// </summary>
    /// <param name="primary"><see langword="true"/> for the primary selection instead of the clipboard.</param>
    /// <param name="mime">The text type to ask for, or <see langword="null"/> for the first text type offered.</param>
    /// <param name="timeoutMs">How long the owner has to write, in milliseconds, or <see langword="null"/> for 2000.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>The text and the offered types. The text is <see langword="null"/> when no text type is offered.</returns>
    public Task<IpcClipboard> ReadClipboardAsync(
        bool primary = false, string? mime = null, int? timeoutMs = null, CancellationToken cancellationToken = default) =>
        CallAsync(
            IpcMethodNames.ClipboardRead,
            new IpcClipboardParams(primary ? "primary" : "clipboard", mime, timeoutMs),
            Json.IpcClipboardParams,
            Json.IpcClipboard,
            cancellationToken);

    /// <summary>
    /// Calls <c>process/spawn</c> with full params.
    /// </summary>
    /// <param name="launch">The program, its arguments and its options.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>The process id and the launch id.</returns>
    public Task<IpcSpawnResult> LaunchAsync(IpcSpawnParams launch, CancellationToken cancellationToken = default) =>
        CallAsync(IpcMethodNames.ProcessSpawn, launch, Json.IpcSpawnParams, Json.IpcSpawnResult, cancellationToken);

    /// <summary>
    /// Calls <c>process/list</c>.
    /// </summary>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>Each tracked launch, running or recently finished.</returns>
    public async Task<IReadOnlyList<IpcProcess>> ListProcessesAsync(CancellationToken cancellationToken = default) =>
        (await CallAsync(IpcMethodNames.ProcessList, Json.IpcProcessList, cancellationToken).ConfigureAwait(false)).Processes.Items();

    /// <summary>
    /// Calls <c>process/kill</c>, which sends <c>SIGTERM</c> to the process group and <c>SIGKILL</c> after the grace time.
    /// </summary>
    /// <param name="launchId">The launch id.</param>
    /// <param name="graceMs">The grace time in milliseconds, or <see langword="null"/> for 2000.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>A task that completes when the signal is sent.</returns>
    public Task KillProcessAsync(long launchId, int? graceMs = null, CancellationToken cancellationToken = default) =>
        CallAsync(
            IpcMethodNames.ProcessKill,
            new IpcProcessKillParams(launchId, graceMs),
            Json.IpcProcessKillParams,
            Json.IpcEmpty,
            cancellationToken);

    /// <summary>
    /// Calls <c>process/log</c>.
    /// </summary>
    /// <param name="launchId">The launch id.</param>
    /// <param name="lines">The number of last lines, or <see langword="null"/> for 50.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>The last lines of the launch's log file.</returns>
    public Task<IpcProcessLog> ProcessLogAsync(long launchId, int? lines = null, CancellationToken cancellationToken = default) =>
        CallAsync(
            IpcMethodNames.ProcessLog,
            new IpcProcessLogParams(launchId, lines),
            Json.IpcProcessLogParams,
            Json.IpcProcessLog,
            cancellationToken);

    /// <summary>
    /// Calls <c>process/spawn</c>, which starts a program with no shell.
    /// </summary>
    /// <param name="argv">The program and its arguments.</param>
    /// <param name="env">Environment variables to add, or <see langword="null"/>.</param>
    /// <param name="cwd">The working directory, or <see langword="null"/>.</param>
    /// <param name="x11Density"><c>native</c> or <c>low</c>, or <see langword="null"/> for the compositor's rules.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>The process id.</returns>
    public async Task<int> SpawnAsync(
        IReadOnlyList<string> argv,
        IReadOnlyDictionary<string, string>? env = null,
        string? cwd = null,
        string? x11Density = null,
        CancellationToken cancellationToken = default) =>
        (await CallAsync(IpcMethodNames.ProcessSpawn, new IpcSpawnParams(argv, env, cwd, x11Density: x11Density), Json.IpcSpawnParams, Json.IpcSpawnResult, cancellationToken)
            .ConfigureAwait(false)).Pid;

    /// <summary>
    /// Calls <c>devices/list</c>.
    /// </summary>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>Each input device.</returns>
    public async Task<IReadOnlyList<IpcDevice>> ListDevicesAsync(CancellationToken cancellationToken = default) =>
        (await CallAsync(IpcMethodNames.DevicesList, Json.IpcDeviceList, cancellationToken).ConfigureAwait(false)).Devices.Items();

    /// <summary>
    /// Calls <c>devices/get</c>.
    /// </summary>
    /// <param name="device">The device id.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>Each setting that the device supports, with its value and source.</returns>
    public Task<IpcDeviceSettings> GetDeviceAsync(ulong device, CancellationToken cancellationToken = default) =>
        CallAsync(IpcMethodNames.DevicesGet, new IpcDeviceParams(device), Json.IpcDeviceParams, Json.IpcDeviceSettings, cancellationToken);

    /// <summary>
    /// Calls <c>devices/set</c> for one device. It writes the runtime layer only.
    /// </summary>
    /// <param name="device">The device id.</param>
    /// <param name="settings">The settings as key and value words.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>One outcome per key: <c>ok</c>, <c>unsupported</c> or <c>invalid</c>.</returns>
    public Task<IReadOnlyList<IpcDeviceOutcome>> SetDeviceAsync(
        ulong device, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default) =>
        SetDevicesAsync(device, null, settings, cancellationToken);

    /// <summary>
    /// Calls <c>devices/set</c> for every device of a type. It writes the runtime layer only.
    /// </summary>
    /// <param name="type">The device type, for example <c>keyboard</c>.</param>
    /// <param name="settings">The settings as key and value words.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>One outcome per device and key. A type with no device answers <c>unsupported</c> with device 0.</returns>
    public Task<IReadOnlyList<IpcDeviceOutcome>> SetDevicesOfTypeAsync(
        string type, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(type);
        return SetDevicesAsync(null, type, settings, cancellationToken);
    }

    /// <summary>
    /// Calls <c>devices/reset</c>, which removes runtime settings so the config value or the default comes back.
    /// </summary>
    /// <param name="device">The device id.</param>
    /// <param name="keys">The keys to reset, or <see langword="null"/> for every runtime key.</param>
    /// <param name="cancellationToken">A token that stops the wait for the reply.</param>
    /// <returns>One outcome per key.</returns>
    public async Task<IReadOnlyList<IpcDeviceOutcome>> ResetDeviceAsync(
        ulong device, IReadOnlyList<string>? keys = null, CancellationToken cancellationToken = default) =>
        (await CallAsync(IpcMethodNames.DevicesReset, new IpcDeviceResetParams(device, keys), Json.IpcDeviceResetParams, Json.IpcDeviceResult, cancellationToken)
            .ConfigureAwait(false)).Results.Items();

    private async Task<IReadOnlyList<IpcDeviceOutcome>> SetDevicesAsync(
        ulong? device, string? type, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var values = new Dictionary<string, JsonElement>(settings.Count, StringComparer.Ordinal);
        foreach (var (key, value) in settings)
        {
            values[key] = JsonSerializer.SerializeToElement(value, Json.String);
        }

        return (await CallAsync(IpcMethodNames.DevicesSet, new IpcDeviceSetParams(device, type, values), Json.IpcDeviceSetParams, Json.IpcDeviceResult, cancellationToken)
            .ConfigureAwait(false)).Results.Items();
    }

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
