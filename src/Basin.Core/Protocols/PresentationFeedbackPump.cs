using Basin.Capabilities;

namespace Basin;

/// <summary>
/// Drives <see cref="PresentationTimeGlobal"/> from output commits, frames and presentation events, for each output in a layout. It stops when the consumer calls the presentation methods itself.
/// </summary>
public sealed class PresentationFeedbackPump : IDisposable, IFrameSink
{
    private readonly PresentationTimeGlobal _presentation;
    private readonly OutputLayout _layout;
    private readonly List<IOutput> _hooked = [];
    private readonly Dictionary<IOutput, Action> _frameHandlers = [];
    private readonly Dictionary<IOutput, Action<OutputStateFields>> _commitHandlers = [];
    private readonly Dictionary<IOutput, Action<ulong, uint, ulong>> _presentHandlers = [];
    private readonly Dictionary<IOutput, Action> _discardHandlers = [];
    private readonly HashSet<IOutput> _discarded = [];
    private readonly Dictionary<IOutput, (ulong TimeNs, uint RefreshNs, ulong Sequence)> _lastPresent = [];
    private bool _consumerEndsFrames;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PresentationFeedbackPump"/> class. It follows the outputs that join and leave the layout.
    /// </summary>
    /// <param name="presentation">The global to drive.</param>
    /// <param name="layout">The layout whose outputs to watch.</param>
    public PresentationFeedbackPump(PresentationTimeGlobal presentation, OutputLayout layout)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(layout);
        _presentation = presentation;
        _layout = layout;
        _layout.Changed += Rehook;
        Rehook();
    }

    /// <summary>
    /// Does nothing. Sampling happens on the output commit instead.
    /// </summary>
    /// <param name="output">The output that the frame is for.</param>
    /// <param name="predictedVblankNanos">The predicted vblank. It is not used.</param>
    public void BeginFrame(IOutput output, long predictedVblankNanos)
    {
    }

    /// <summary>
    /// Sends feedback for the main surfaces of an output when a frame clock reports the presentation. After the first call, output frame events no longer send feedback.
    /// </summary>
    /// <param name="output">The output that presented the frame.</param>
    /// <param name="presentedNanos">The presentation time on the monotonic clock in nanoseconds, or 0 or less to use the current time.</param>
    public void EndFrame(IOutput output, long presentedNanos)
    {
        ArgumentNullException.ThrowIfNull(output);
        _consumerEndsFrames = true;
        if (_disposed || _presentation.ConsumerPresents)
        {
            return;
        }

        if (_discarded.Remove(output))
        {
            _lastPresent.Remove(output);
            _presentation.DiscardOutputCore(output);
            return;
        }

        if (presentedNanos <= 0)
        {
            _presentation.PresentOutputNowCore(output);
            return;
        }

        var refreshNs = _lastPresent.Remove(output, out var present)
            ? present.RefreshNs
            : output.CurrentMode.RefreshIntervalNanoseconds;
        _presentation.PresentOutputCore(
            output, (ulong)presentedNanos, refreshNs, present.Sequence, PresentedFlags.Vsync | PresentedFlags.HwClock);
    }

    /// <summary>
    /// Removes the handlers from all outputs and from the layout.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _layout.Changed -= Rehook;
        Unhook();
    }

    private void Unhook()
    {
        foreach (var output in _hooked)
        {
            if (_frameHandlers.Remove(output, out var onFrame))
            {
                output.Frame -= onFrame;
            }

            if (_commitHandlers.Remove(output, out var onCommit))
            {
                output.Committed -= onCommit;
            }

            if (output is IPresentingOutput presenting)
            {
                if (_presentHandlers.Remove(output, out var onPresent))
                {
                    presenting.PresentedOnScreen -= onPresent;
                }

                if (_discardHandlers.Remove(output, out var onDiscard))
                {
                    presenting.PresentationDiscarded -= onDiscard;
                }
            }
        }

        _hooked.Clear();
        _lastPresent.Clear();
        _discarded.Clear();
    }

    private void Rehook()
    {
        if (_disposed)
        {
            return;
        }

        Unhook();
        foreach (var (output, _) in _layout.Outputs)
        {
            var target = output;
            Action onFrame = () => OnFrame(target);
            target.Frame += onFrame;
            _frameHandlers[target] = onFrame;

            Action<OutputStateFields> onCommit = fields => OnCommitted(target, fields);
            target.Committed += onCommit;
            _commitHandlers[target] = onCommit;

            if (target is IPresentingOutput presenting)
            {
                Action<ulong, uint, ulong> onPresent = (timeNs, refreshNs, sequence) =>
                    _lastPresent[target] = (timeNs, refreshNs, sequence);
                presenting.PresentedOnScreen += onPresent;
                _presentHandlers[target] = onPresent;

                Action onDiscard = () => _discarded.Add(target);
                presenting.PresentationDiscarded += onDiscard;
                _discardHandlers[target] = onDiscard;
            }

            _hooked.Add(target);
        }
    }

    private void OnCommitted(IOutput output, OutputStateFields fields)
    {
        if (_disposed || _presentation.ConsumerSamples || (fields & OutputStateFields.Buffer) == 0)
        {
            return;
        }

        _presentation.SampleOutputCore(output);
    }

    private void OnFrame(IOutput output)
    {
        if (_disposed || _consumerEndsFrames || _presentation.ConsumerPresents)
        {
            return;
        }

        if (_discarded.Remove(output))
        {
            _lastPresent.Remove(output);
            _presentation.DiscardOutputCore(output);
            return;
        }

        if (_lastPresent.Remove(output, out var present))
        {
            _presentation.PresentOutputCore(
                output,
                present.TimeNs,
                present.RefreshNs,
                present.Sequence,
                PresentedFlags.Vsync | PresentedFlags.HwClock | PresentedFlags.HwCompletion);
            return;
        }

        _presentation.PresentOutputNowCore(output);
    }
}
