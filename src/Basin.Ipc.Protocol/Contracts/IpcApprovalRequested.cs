namespace Basin.Ipc;

/// <summary>
/// The data of the <c>approval/requested</c> event: a call that an interceptor holds until a subscriber answers it.
/// </summary>
/// <param name="Id">The request id that <c>approval/answer</c> takes.</param>
/// <param name="Method">The name of the held method.</param>
/// <param name="Arguments">The params of the held call as JSON. It is an empty object when the call had none.</param>
/// <param name="Reason">A sentence that the compositor wrote to say why it asks.</param>
public readonly record struct IpcApprovalRequested(long Id, string Method, IpcRawJson Arguments, string Reason);
