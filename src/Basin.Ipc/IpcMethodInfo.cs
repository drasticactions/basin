namespace Basin.Ipc;

public sealed record IpcMethodInfo(
    string Description,
    string? ParamsSchema,
    IpcMethodTraits Traits);
