using Wayland.Server;

namespace Basin.Capabilities;

public readonly record struct ClientInfo(ulong Id, int Pid, uint Uid, uint Gid, WlClient Client);
