namespace Basin.WindowManager;

/// <summary>
/// The current seat and output of a manager. It falls back to the first seat and output when the current one goes away. It tracks every output and seat on the layer shell.
/// </summary>
public sealed class WmCurrents
{
    private readonly RiverWindowManager _wm;
    private bool _layerDefaultSet;

    /// <summary>
    /// Initializes a new instance of the <see cref="WmCurrents"/> class.
    /// </summary>
    /// <param name="wm">The manager whose layer shell this object uses.</param>
    /// <exception cref="ArgumentNullException"><paramref name="wm"/> is <see langword="null"/>.</exception>
    public WmCurrents(RiverWindowManager wm)
    {
        ArgumentNullException.ThrowIfNull(wm);
        _wm = wm;
    }

    /// <summary>
    /// Gets or sets the current seat, or <see langword="null"/> when there is none.
    /// </summary>
    public WmSeat? Seat { get; set; }

    /// <summary>
    /// Gets or sets the current output, or <see langword="null"/> when there is none. A direct set does not move the layer default. <see cref="Follow"/> does.
    /// </summary>
    public WmOutput? Output { get; set; }

    /// <summary>
    /// Gets a value indicating whether the current output is also the default output for new layer surfaces, set once for each new current output.
    /// </summary>
    public bool SetsLayerDefault { get; init; }

    /// <summary>
    /// Makes an output current, for example when focus moves to it. With <see cref="SetsLayerDefault"/>, it also moves the layer default when the output changes.
    /// </summary>
    /// <param name="output">The new current output.</param>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The layer default must move and no manage sequence is in progress.</exception>
    public void Follow(WmOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (SetsLayerDefault && _wm.LayerShell is not null
            && (!_layerDefaultSet || !ReferenceEquals(Output, output)))
        {
            output.SetDefaultForLayerSurfaces();
            _layerDefaultSet = true;
        }

        Output = output;
    }

    /// <summary>
    /// Replaces a missing or removed seat or output with the first one present, and sets the layer default when it is due. It tracks every output and seat on the layer shell.
    /// </summary>
    /// <param name="context">The manage sequence in progress.</param>
    /// <param name="outputs">The outputs to choose from and track, usually <see cref="ManageContext.Outputs"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> or <paramref name="outputs"/> is <see langword="null"/>.</exception>
    public void Update(ManageContext context, IReadOnlyList<WmOutput> outputs)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(outputs);
        if (Seat is null or { IsRemoved: true })
        {
            Seat = context.Seats.Count > 0 ? context.Seats[0] : null;
        }

        if (Output is null or { IsRemoved: true })
        {
            Output = outputs.Count > 0 ? outputs[0] : null;
            _layerDefaultSet = false;
        }

        if (SetsLayerDefault && !_layerDefaultSet && _wm.LayerShell is not null
            && Output is { IsRemoved: false } defaultOutput)
        {
            defaultOutput.SetDefaultForLayerSurfaces();
            _layerDefaultSet = true;
        }

        if (_wm.LayerShell is { } shell)
        {
            foreach (var output in outputs)
            {
                shell.Track(output);
            }

            foreach (var seat in context.Seats)
            {
                shell.Track(seat);
            }
        }
    }
}
