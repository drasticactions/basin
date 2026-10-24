namespace Basin.Capabilities;

/// <summary>
/// Receives the start and the end of each frame that an <see cref="IFrameClock"/> passes on.
/// </summary>
public interface IFrameSink
{
    /// <summary>
    /// Marks the latching deadline of a frame. A content update that arrives after this call goes into a later frame.
    /// </summary>
    /// <param name="output">The output that the frame is for.</param>
    /// <param name="predictedVblankNanos">The predicted vblank on the monotonic clock in nanoseconds, or 0 when it is not known. A sink must read 0 as "apply now".</param>
    void BeginFrame(IOutput output, long predictedVblankNanos);

    /// <summary>
    /// Reports that the frame was presented.
    /// </summary>
    /// <param name="output">The output that presented the frame.</param>
    /// <param name="presentedNanos">The presentation time on the monotonic clock in nanoseconds, or 0 when it is not known.</param>
    void EndFrame(IOutput output, long presentedNanos);
}
