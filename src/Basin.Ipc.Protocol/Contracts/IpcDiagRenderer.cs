namespace Basin.Ipc;

/// <summary>
/// The reply of <c>diag/renderer</c>: the renderer that draws now, how it finds a lost device, and the number of replaced renderers.
/// </summary>
/// <param name="Renderer">The type name of the renderer, or <see langword="null"/> before one exists.</param>
/// <param name="LossDetection">How the renderer finds a lost device: never, reset-notification, errors or device-status.</param>
/// <param name="Rebuilds">The number of lost renderers that were replaced.</param>
public readonly record struct IpcDiagRenderer(string? Renderer, string LossDetection, long Rebuilds);
