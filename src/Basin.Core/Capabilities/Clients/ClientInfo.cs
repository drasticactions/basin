using Wayland.Server;

namespace Basin.Capabilities;

/// <summary>
/// One connected Wayland client and the credentials of its socket.
/// </summary>
/// <param name="Id">An id that the directory gives the client. It is unique for the life of the directory and is never reused.</param>
/// <param name="Pid">The process id from the socket credentials, or 0 when the credentials are not available.</param>
/// <param name="Uid">The user id from the socket credentials, or 0 when the credentials are not available.</param>
/// <param name="Gid">The group id from the socket credentials, or 0 when the credentials are not available.</param>
/// <param name="Client">The client connection.</param>
public readonly record struct ClientInfo(ulong Id, int Pid, uint Uid, uint Gid, WlClient Client);
