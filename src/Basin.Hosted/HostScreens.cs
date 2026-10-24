using Basin.Desktop;

namespace Basin.Hosted;

/// <summary>
/// The <c>wl_output</c> globals that describe the host desktop to clients. It shows either the host monitors from <see cref="Apply"/> or the outputs from <see cref="Advertise"/>, never both.
/// A placeholder screen stands in until the first real screen arrives.
/// </summary>
public sealed class HostScreens : IDisposable
{
    private readonly BasinCompositorHost _host;
    private readonly Dictionary<string, Row> _rows = [];
    private readonly FractionalScaleManager? _fractional;
    private bool _sawRealScreens;
    private bool _disposed;

    private sealed class Row
    {
        public required Backend.Hosted.HostedOutput Output;
        public required OutputGlobal Global;
        public required HostScreenInfo Info;
        public double? Noted;
        public bool Advertised;
        public double Effective => Noted ?? Info.Scaling;
    }

    private readonly List<string> _advertised = [];

    internal HostScreens(BasinCompositorHost host)
    {
        _host = host;
        _fractional = host.Services.Find<FractionalScaleManager>();
        Add(new HostScreenInfo("waylonia-placeholder", "WAYLONIA-1", 0, 0, 1920, 1080, 1.0, true));
        SyncDefaultScale();
    }

    /// <summary>
    /// Gets a snapshot of the screen rows, including the placeholder while it is present.
    /// </summary>
    public IReadOnlyCollection<HostScreenInfo> Current
    {
        get
        {
            var list = new List<HostScreenInfo>(_rows.Count);
            foreach (var row in _rows.Values)
            {
                list.Add(row.Info);
            }

            return list;
        }
    }

    /// <summary>
    /// Gets the key of the first output that the consumer advertised. It is <see langword="null"/> when the consumer advertised none.
    /// </summary>
    public string? AdvertisedKey => _advertised.Count > 0 ? _advertised[0] : null;

    /// <summary>
    /// Gets the keys of all outputs that the consumer advertised, in the order of advertisement.
    /// </summary>
    public IReadOnlyList<string> AdvertisedKeys => _advertised;

    /// <summary>
    /// Publishes an output that the consumer owns as a screen row, or updates the row when the key is already advertised for the same output.
    /// The row never resizes or destroys the output.
    /// </summary>
    /// <param name="output">The output to publish.</param>
    /// <param name="info">The description of the row. Its key identifies the row.</param>
    /// <exception cref="InvalidOperationException">A row with the same key holds a different output.</exception>
    /// <exception cref="ObjectDisposedException">The screens are disposed.</exception>
    public void Advertise(Backend.Hosted.HostedOutput output, HostScreenInfo info)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(info);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_rows.TryGetValue(info.Key, out var row))
        {
            if (!ReferenceEquals(row.Output, output))
            {
                throw new InvalidOperationException(
                    $"'{info.Key}' is already a host screen row; an advertised output needs a key of its own.");
            }

            var moved = LogicalX(row.Info) != LogicalX(info) || LogicalY(row.Info) != LogicalY(info);
            if (moved)
            {
                _host.Layout.Remove(output);
                _host.Layout.Add(output, LogicalX(info), LogicalY(info));
            }

            row.Info = info;
            SyncDefaultScale();
            return;
        }

        if (!_sawRealScreens)
        {
            _sawRealScreens = true;
            RemoveRow("waylonia-placeholder");
        }

        var global = new OutputGlobal(_host.Display, output);
        _host.Layout.Add(output, LogicalX(info), LogicalY(info));
        _rows[info.Key] = new Row { Output = output, Global = global, Info = info, Advertised = true };
        _host.NotifyOutputAdvertised(global, output);
        _advertised.Add(info.Key);
        SyncDefaultScale();
    }

    internal void NotifyReconfigured(Backend.Hosted.HostedOutput output)
    {
        if (_disposed || RowOf(output) is not { } row)
        {
            return;
        }

        var mode = output.CurrentMode;
        var swapped = output.Transform.SwapsAxes();
        Advertise(output, row.Info with
        {
            Width = swapped ? mode.Height : mode.Width,
            Height = swapped ? mode.Width : mode.Height,
            Scaling = output.Scale,
            Transform = output.Transform,
        });
    }

    /// <summary>
    /// Reconciles the rows with a snapshot of the host monitors: it adds new keys, updates changed rows and retires missing ones.
    /// An empty list does nothing. A repeated key counts once.
    /// </summary>
    /// <param name="screens">The host monitors.</param>
    /// <exception cref="InvalidOperationException">The consumer advertised an output of its own.</exception>
    /// <exception cref="ObjectDisposedException">The screens are disposed.</exception>
    public void Apply(IReadOnlyList<HostScreenInfo> screens)
    {
        ArgumentNullException.ThrowIfNull(screens);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (AdvertisedKey is { } advertised)
        {
            throw new InvalidOperationException(
                $"host screens cannot be applied while the output '{advertised}' is advertised by the consumer; " +
                "a session shows either the host's monitors or outputs of its own, never both.");
        }

        if (screens.Count == 0)
        {
            return;
        }

        if (!_sawRealScreens)
        {
            _sawRealScreens = true;
            RemoveRow("waylonia-placeholder");
        }

        var seen = new HashSet<string>();
        foreach (var info in screens)
        {
            if (!seen.Add(info.Key))
            {
                continue;
            }

            if (_rows.TryGetValue(info.Key, out var row))
            {
                Update(row, info);
            }
            else
            {
                Add(info);
            }
        }

        foreach (var key in _rows.Keys.ToArray())
        {
            if (!seen.Contains(key) && key != "waylonia-placeholder")
            {
                RemoveRow(key);
            }
        }

        SyncDefaultScale();
    }

    private void SyncDefaultScale()
    {
        if (_fractional is { } fractional)
        {
            fractional.DefaultScale = DefaultScaling;
        }
    }

    /// <summary>
    /// Puts a surface tree on the screen with <paramref name="key"/> and announces that screen's scale to it.
    /// </summary>
    /// <param name="surface">The root of the surface tree.</param>
    /// <param name="key">The screen key, or <see langword="null"/> to remove the tree from every screen.</param>
    public void EnterScreen(Surface surface, string? key)
    {
        RefreshPresence(surface, key);
        if (key is not null && _rows.TryGetValue(key, out var entered))
        {
            AnnounceScaleTree(surface, entered.Effective);
        }
    }

    /// <summary>
    /// Sets the output presence of a surface tree so that it is on the screen with <paramref name="key"/> and on no other. It announces no scale.
    /// </summary>
    /// <param name="surface">The root of the surface tree.</param>
    /// <param name="key">The screen key, or <see langword="null"/> to remove the tree from every screen.</param>
    public void RefreshPresence(Surface surface, string? key)
    {
        ArgumentNullException.ThrowIfNull(surface);
        foreach (var (rowKey, row) in _rows)
        {
            SetPresenceTree(surface, row.Global, rowKey == key);
        }
    }

    private static void SetPresenceTree(Surface surface, OutputGlobal global, bool inside)
    {
        surface.SetOutputPresence(global, inside);
        foreach (var below in surface.SubsurfacesBelow)
        {
            SetPresenceTree(below.Surface, global, inside);
        }

        foreach (var above in surface.SubsurfacesAbove)
        {
            SetPresenceTree(above.Surface, global, inside);
        }
    }

    private void AnnounceScaleTree(Surface surface, double scale)
    {
        _fractional?.AnnounceScale(surface, scale);
        foreach (var below in surface.SubsurfacesBelow)
        {
            AnnounceScaleTree(below.Surface, scale);
        }

        foreach (var above in surface.SubsurfacesAbove)
        {
            AnnounceScaleTree(above.Surface, scale);
        }
    }

    /// <summary>
    /// Records the scale that a host window observed on a screen. A changed scale resizes the row's output so that the logical size stays the same.
    /// An advertised row records the scale and keeps its mode. An unknown key or a scale of zero or less does nothing.
    /// </summary>
    /// <param name="key">The screen key.</param>
    /// <param name="scale">The observed scale.</param>
    /// <exception cref="ObjectDisposedException">The screens are disposed.</exception>
    public void NoteWindowScale(string key, double scale)
    {
        ArgumentNullException.ThrowIfNull(key);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (scale <= 0 || !_rows.TryGetValue(key, out var row))
        {
            return;
        }

        var snapped = OutputScaling.Snap(scale);
        if (row.Effective == snapped || row.Advertised)
        {
            row.Noted = snapped;
            SyncDefaultScale();
            return;
        }

        row.Noted = snapped;
        var (width, height) = ModeOf(row.Info, snapped);
        row.Output.Resize(width, height, snapped);
        SyncDefaultScale();
    }

    /// <summary>
    /// Gets the effective scale of a screen: the observed window scale when there is one, or else the host scale.
    /// </summary>
    /// <param name="key">The screen key.</param>
    /// <returns>The scale, or 1 for an unknown key.</returns>
    public double ScalingOf(string key) =>
        _rows.TryGetValue(key, out var row) ? row.Effective : 1.0;

    /// <summary>
    /// Gets the effective scale of the default screen, which is the primary one, else the first with an observed scale, else the first row. It is 1 with no rows.
    /// </summary>
    public double DefaultScaling
    {
        get
        {
            Row? first = null;
            Row? noted = null;
            foreach (var row in _rows.Values)
            {
                first ??= row;
                if (row.Info.Primary)
                {
                    return row.Effective;
                }

                if (row.Noted is not null)
                {
                    noted ??= row;
                }
            }

            return (noted ?? first)?.Effective ?? 1.0;
        }
    }

    /// <summary>
    /// Finds the output global of a screen.
    /// </summary>
    /// <param name="key">The screen key.</param>
    /// <returns>The global, or <see langword="null"/> for an unknown key.</returns>
    public OutputGlobal? GlobalFor(string key) =>
        _rows.TryGetValue(key, out var row) ? row.Global : null;

    /// <summary>
    /// Gets the key of the default screen, chosen as for <see cref="DefaultScaling"/>. It is <see langword="null"/> with no rows.
    /// </summary>
    public string? DefaultKey
    {
        get
        {
            string? first = null;
            string? noted = null;
            foreach (var (key, row) in _rows)
            {
                first ??= key;
                if (row.Info.Primary)
                {
                    return key;
                }

                if (row.Noted is not null)
                {
                    noted ??= key;
                }
            }

            return noted ?? first;
        }
    }

    /// <summary>
    /// Finds the key of the screen that an output global belongs to.
    /// </summary>
    /// <param name="global">The output global, or <see langword="null"/>.</param>
    /// <returns>The screen key, or <see cref="DefaultKey"/> when the global is <see langword="null"/> or not a screen.</returns>
    public string? KeyOf(OutputGlobal? global)
    {
        if (global is null)
        {
            return DefaultKey;
        }

        foreach (var (key, row) in _rows)
        {
            if (ReferenceEquals(row.Global, global))
            {
                return key;
            }
        }

        return DefaultKey;
    }

    private void Add(HostScreenInfo info)
    {
        var (width, height) = ModeOf(info, info.Scaling);
        var output = _host.Backend.CreateOutput(
            new OutputMode(width, height, 60_000),
            info.Scaling,
            info.Name);
        var global = new OutputGlobal(_host.Display, output);
        _host.Layout.Add(output, LogicalX(info), LogicalY(info));
        _rows[info.Key] = new Row { Output = output, Global = global, Info = info };
        _host.NotifyOutputAdvertised(global, output);
    }

    private void Update(Row row, HostScreenInfo info)
    {
        if (row.Info == info)
        {
            return;
        }

        var moved = LogicalX(row.Info) != LogicalX(info) || LogicalY(row.Info) != LogicalY(info);
        var (width, height) = ModeOf(info, row.Noted ?? info.Scaling);
        row.Output.Resize(width, height, row.Noted ?? info.Scaling);
        if (moved)
        {
            _host.Layout.Remove(row.Output);
            _host.Layout.Add(row.Output, LogicalX(info), LogicalY(info));
        }

        row.Info = info;
    }

    private void RemoveRow(string key)
    {
        if (!_rows.Remove(key, out var row))
        {
            return;
        }

        foreach (var surface in _host.Services.Require<CompositorGlobal>().Surfaces)
        {
            surface.SetOutputPresence(row.Global, false);
        }

        _host.Layout.Remove(row.Output);
        var output = row.Output;
        _host.NotifyOutputWithdrawn(row.Global);
        row.Global.Retire();
        if (row.Advertised)
        {
            _advertised.Remove(key);
        }
        else
        {
            output.Destroy();
        }
    }

    private Row? RowOf(Backend.Hosted.HostedOutput output)
    {
        foreach (var key in _advertised)
        {
            if (_rows.TryGetValue(key, out var row) && ReferenceEquals(row.Output, output))
            {
                return row;
            }
        }

        return null;
    }

    /// <summary>
    /// Retires a row that <see cref="Advertise"/> published. The output itself stays with the consumer. A key that is not advertised does nothing.
    /// </summary>
    /// <param name="key">The key of the advertised row.</param>
    public void Withdraw(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_rows.TryGetValue(key, out var row) && row.Advertised)
        {
            RemoveRow(key);
        }
    }

    private static (int Width, int Height) ModeOf(HostScreenInfo info, double scale) => (
        Math.Max(1, (int)Math.Round(info.Width / info.Scaling * scale)),
        Math.Max(1, (int)Math.Round(info.Height / info.Scaling * scale)));

    private static int LogicalX(HostScreenInfo info) => (int)Math.Round(info.X / info.Scaling);

    private static int LogicalY(HostScreenInfo info) => (int)Math.Round(info.Y / info.Scaling);

    /// <summary>
    /// Removes every row from the layout and disposes its global. It destroys the outputs that the screens created, but not the advertised ones.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var row in _rows.Values)
        {
            _host.Layout.Remove(row.Output);
            row.Global.Dispose();
            if (!row.Advertised)
            {
                row.Output.Destroy();
            }
        }

        _rows.Clear();
        _advertised.Clear();
    }
}
