using Basin.Ipc;

namespace BasinMcp;

internal readonly record struct McpWaitEventResult(IReadOnlyList<McpCaughtEvent> Events, IpcRawJson Then);
