using Basin.Desktop;

namespace Basin.Hosted;

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

    private string? _advertisedKey;

    internal HostScreens(BasinCompositorHost host)
    {
        _host = host;
        _fractional = host.Services.Find<FractionalScaleManager>();
        Add(new HostScreenInfo("waylonia-placeholder", "WAYLONIA-1", 0, 0, 1920, 1080, 1.0, true));
        SyncDefaultScale();
    }

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

    public string? AdvertisedKey => _advertisedKey;

    public void Advertise(Backend.Hosted.HostedOutput output, HostScreenInfo info)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(info);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_advertisedKey is { } current && current != info.Key)
        {
            throw new InvalidOperationException(
                $"the output '{current}' is already advertised; a host advertises one output of its own.");
        }

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
        _advertisedKey = info.Key;
        SyncDefaultScale();
    }

    internal void NotifyReconfigured(Backend.Hosted.HostedOutput output)
    {
        if (_disposed || _advertisedKey is not { } key || !_rows.TryGetValue(key, out var row) ||
            !ReferenceEquals(row.Output, output))
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

    public void Apply(IReadOnlyList<HostScreenInfo> screens)
    {
        ArgumentNullException.ThrowIfNull(screens);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_advertisedKey is { } advertised)
        {
            throw new InvalidOperationException(
                $"host screens cannot be applied while the output '{advertised}' is advertised by the consumer; " +
                "a session shows either the host's monitors or one output of its own, never both.");
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

    public void EnterScreen(Surface surface, string? key)
    {
        RefreshPresence(surface, key);
        if (key is not null && _rows.TryGetValue(key, out var entered))
        {
            AnnounceScaleTree(surface, entered.Effective);
        }
    }

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

    public double ScalingOf(string key) =>
        _rows.TryGetValue(key, out var row) ? row.Effective : 1.0;

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

    public OutputGlobal? GlobalFor(string key) =>
        _rows.TryGetValue(key, out var row) ? row.Global : null;

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
        row.Global.Retire();
        if (row.Advertised)
        {
            _advertisedKey = null;
        }
        else
        {
            output.Destroy();
        }
    }

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
        _advertisedKey = null;
    }
}
