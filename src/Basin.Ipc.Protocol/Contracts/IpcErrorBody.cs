namespace Basin.Ipc;

/// <summary>
/// The <c>error</c> object of an error reply.
/// </summary>
/// <param name="Code">The error code, one of <see cref="IpcErrorCodes"/> or a code of the consumer.</param>
/// <param name="Message">A sentence that says why the call failed.</param>
public readonly record struct IpcErrorBody(string Code, string Message);
