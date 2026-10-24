namespace Basin.Ipc;

/// <summary>
/// The reply of <c>idle/inhibit</c>. The inhibitor belongs to the connection, and it goes when the connection closes.
/// </summary>
/// <param name="Token">The handle that <c>idle/uninhibit</c> takes in <see cref="IpcTokenParams.Token"/> to release the inhibitor.</param>
public readonly record struct IpcIdleToken(string Token);
