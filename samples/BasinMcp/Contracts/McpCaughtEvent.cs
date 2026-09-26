using Basin.Ipc;

namespace BasinMcp;

internal readonly record struct McpCaughtEvent(string Event, IpcRawJson Data);
