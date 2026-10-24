namespace Basin.Ipc;

/// <summary>
/// The reply of <c>outputs/test</c>.
/// </summary>
/// <param name="Ok"><see langword="true"/> when the outputs can take the configuration. The test changes nothing.</param>
public readonly record struct IpcOutputTestResult(bool Ok);
