namespace Basin;

/// <summary>
/// The exception that a renderer throws when its GPU device or context is lost. A caller on the frame path can then tell a loss from a bug.
/// </summary>
public sealed class RendererLostException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RendererLostException"/> class with a default message.
    /// </summary>
    public RendererLostException()
        : base("The renderer lost its device.")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RendererLostException"/> class.
    /// </summary>
    /// <param name="message">The message that tells how the renderer found the loss.</param>
    public RendererLostException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RendererLostException"/> class with the error that showed the loss.
    /// </summary>
    /// <param name="message">The message that tells how the renderer found the loss.</param>
    /// <param name="innerException">The error that showed the loss.</param>
    public RendererLostException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
