namespace Basin.Ipc;

/// <summary>
/// Runs one method call on the compositor thread.
/// </summary>
/// <param name="parameters">The call's params, read through the contract or the getters.</param>
/// <param name="reply">The reply that the handler writes a result or an error into, or defers.</param>
public delegate void IpcHandler(ref IpcParams parameters, IpcReply reply);
