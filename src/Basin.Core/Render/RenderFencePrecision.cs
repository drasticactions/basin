using Pixman;

namespace Basin;

/// <summary>
/// Tells how much the signal fence of a render pass promises.
/// </summary>
public enum RenderFencePrecision
{
    /// <summary>
    /// The renderer gives no fence.
    /// </summary>
    None,

    /// <summary>
    /// The renderer drains its whole device before it signals, so the signal fence is signaled when <see cref="IRenderPass.Submit"/> returns.
    /// </summary>
    Context,

    /// <summary>
    /// The signal fence signals when the work of this pass completes, which can be after <see cref="IRenderPass.Submit"/> returns.
    /// </summary>
    PerSubmission,
}
