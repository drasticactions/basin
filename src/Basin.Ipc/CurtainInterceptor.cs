using Basin.Capabilities;

namespace Basin.Ipc;

/// <summary>
/// Holds every call that returns screen pixels while the screen curtain is drawn, and asks a person through an <see cref="IApprovalBroker"/>.
/// An allowed call sees the screen through a curtain pass until its reply is complete. The outputs stay black.
/// A call from the line front runs at once, because the person who owns the compositor's input wrote it.
/// </summary>
public sealed class CurtainInterceptor : IIpcInterceptor
{
    private const string Reason = "The screen curtain is drawn. The call can see the screen only if you allow it.";

    private readonly IScreenCurtain _curtain;
    private readonly IApprovalBroker _broker;
    private readonly IIpcInterceptor? _inner;
    private int _passes;

    /// <summary>
    /// Initializes a new instance of the <see cref="CurtainInterceptor"/> class.
    /// </summary>
    /// <param name="curtain">The screen curtain.</param>
    /// <param name="broker">The broker that asks a person.</param>
    /// <param name="inner">An interceptor that sees every call first, or <see langword="null"/>.</param>
    public CurtainInterceptor(IScreenCurtain curtain, IApprovalBroker broker, IIpcInterceptor? inner = null)
    {
        ArgumentNullException.ThrowIfNull(curtain);
        ArgumentNullException.ThrowIfNull(broker);
        _curtain = curtain;
        _broker = broker;
        _inner = inner;
    }

    /// <summary>
    /// Tells whether a method returns screen pixels.
    /// </summary>
    /// <param name="method">The method name.</param>
    /// <returns><see langword="true"/> for the capture methods, <c>diag/record</c>, <c>diag/record-window</c> and <c>diag/compare</c>.</returns>
    public static bool ShowsPixels(string method) =>
        method.StartsWith("capture/", StringComparison.Ordinal)
        || method is IpcMethodNames.DiagRecord or IpcMethodNames.DiagCompare or IpcMethodNames.DiagRecordWindow;

    /// <inheritdoc/>
    public IpcDecision Before(string method, ReadOnlySpan<byte> parameters, IpcCallContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var decision = _inner?.Before(method, parameters, context) ?? IpcDecision.Allow;
        if (decision.Kind != IpcDecisionKind.Allow || !_curtain.IsDrawn || !ShowsPixels(method))
        {
            return decision;
        }

        if (context.FromLineFront || _broker.IsAllowedForRun(method))
        {
            Open();
            return IpcDecision.Allow;
        }

        var approval = _broker.Request(context.Hold(), Reason);
        approval.Allowing += _ => Open();
        return IpcDecision.Defer;
    }

    /// <inheritdoc/>
    public void After(string method, IpcCallOutcome outcome, TimeSpan elapsed, IpcCallContext context)
    {
        if (ShowsPixels(method) && outcome.ErrorCode != IpcErrorCodes.Refused && _passes > 0)
        {
            _passes--;
            _curtain.EndPass();
        }

        _inner?.After(method, outcome, elapsed, context);
    }

    private void Open()
    {
        _passes++;
        _curtain.BeginPass();
    }
}
