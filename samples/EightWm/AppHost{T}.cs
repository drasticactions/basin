using Basin;

namespace EightWm;

internal sealed class AppHost<T>
    where T : class, IShellApp
{
    private readonly List<T?> _slots = [];
    private readonly List<T> _cells = [];
    private readonly List<T> _mru = [];
    private readonly List<int> _widths = [];
    private T? _active;
    private int _vacant = -1;

    public IReadOnlyList<T> Cells => _cells;

    public IReadOnlyList<T> Mru => _mru;

    public IReadOnlyList<int> Widths => _widths;

    public int MaxCells { get; set; } = 4;

    public int MinWidth { get; set; } = 500;

    public bool IsEmpty => _cells.Count == 0;

    public int SlotCount => _slots.Count;

    public bool HasVacancy => _vacant >= 0;

    public int VacantSlot => _vacant;

    public Box VacantArea { get; private set; }

    public T? Active => _active;

    public int ActiveIndex => _active is null ? 0 : Math.Max(0, _cells.IndexOf(_active));

    public bool Holds(T app) => _cells.Contains(app);

    public void Touch(T app)
    {
        if (_measuring)
        {
            return;
        }

        _mru.Remove(app);
        _mru.Insert(0, app);
    }

    public void Forget(T app)
    {
        _mru.Remove(app);
        Release(app);
    }

    public bool Adopt(T app, int at = -1)
    {
        if (_cells.Contains(app))
        {
            return false;
        }

        Touch(app);
        while (_cells.Count >= MaxCells && _cells.Count > 0)
        {
            Eject(_cells[^1]);
        }

        var index = at < 0 || at > _slots.Count ? _slots.Count : at;
        _slots.Insert(index, app);
        _widths.Insert(Math.Min(index, _widths.Count), 0);
        _active = app;
        Sync();
        return true;
    }

    public void Replace(T app)
    {
        Touch(app);
        if (_cells.Contains(app))
        {
            _active = app;
            return;
        }

        if (_vacant >= 0)
        {
            _slots[_vacant] = app;
            _active = app;
            Sync();
            return;
        }

        if (_slots.Count == 0)
        {
            _slots.Add(app);
            _widths.Add(0);
            _active = app;
            Sync();
            return;
        }

        var outgoing = _active ?? _cells[^1];
        _slots[_slots.IndexOf(outgoing)] = app;
        _active = app;
        Sync();
        outgoing.Hidden();
    }

    public void Eject(T app)
    {
        if (!_cells.Contains(app))
        {
            return;
        }

        Release(app);
        if (!_measuring)
        {
            app.Hidden();
        }
    }

    public void ClearVacancy()
    {
        if (_vacant < 0)
        {
            return;
        }

        DropSlot(_vacant);
    }

    public void Activate(T app)
    {
        if (_cells.Contains(app))
        {
            _active = app;
        }

        Touch(app);
    }

    public T? Previous()
    {
        for (var i = 0; i < _mru.Count; i++)
        {
            if (!_cells.Contains(_mru[i]))
            {
                return _mru[i];
            }
        }

        return null;
    }

    public T? Cycle(int steps)
    {
        if (_mru.Count == 0)
        {
            return null;
        }

        var index = ((steps % _mru.Count) + _mru.Count) % _mru.Count;
        return _mru[index];
    }

    public int Gutter { get; set; } = 22;

    public bool Portrait { get; private set; }

    public Box Area { get; private set; }

    private int _span;

    public void Layout(in Box area, bool portrait)
    {
        Area = area;
        Portrait = portrait;
        VacantArea = default;
        if (_slots.Count == 0)
        {
            _span = 0;
            return;
        }

        _span = SpanFor(_slots.Count);
        EjectUntilFits();
        EnsureWidths(_span);

        var offset = 0;
        for (var i = 0; i < _slots.Count; i++)
        {
            var extent = _widths[i];
            var box = portrait
                ? new Box(area.X, area.Y + offset, area.Width, extent)
                : new Box(area.X + offset, area.Y, extent, area.Height);
            if (_slots[i] is { } app)
            {
                if (_measuring)
                {
                    if (ReferenceEquals(app, _measured))
                    {
                        MeasuredArea = box;
                    }
                }
                else
                {
                    app.Placed(box);
                }
            }
            else
            {
                VacantArea = box;
            }

            offset += extent + Gutter;
        }
    }

    private readonly List<T?> _slotBackup = [];
    private readonly List<int> _widthBackup = [];
    private bool _measuring;
    private T? _measured;

    public Box MeasuredArea { get; private set; }

    public bool TryMeasureSplit(T app, int at, double fraction, out Box box)
    {
        box = default;
        if (Area.Width <= 0 || Area.Height <= 0)
        {
            return false;
        }

        _slotBackup.Clear();
        _slotBackup.AddRange(_slots);
        _widthBackup.Clear();
        _widthBackup.AddRange(_widths);
        var active = _active;
        var vacantArea = VacantArea;
        var span = _span;

        _measuring = true;
        _measured = app;
        MeasuredArea = default;
        try
        {
            if (_cells.Contains(app))
            {
                Release(app);
            }

            if (TrySplit(app, at, fraction))
            {
                Layout(Area, Portrait);
                box = MeasuredArea;
            }
        }
        finally
        {
            _measuring = false;
            _measured = null;
            MeasuredArea = default;
            _slots.Clear();
            _slots.AddRange(_slotBackup);
            _widths.Clear();
            _widths.AddRange(_widthBackup);
            Sync();
            _active = active;
            VacantArea = vacantArea;
            _span = span;
        }

        return !box.IsEmpty;
    }

    public Box GutterBox(int splitter)
    {
        if (splitter < 0 || splitter >= _slots.Count - 1)
        {
            return default;
        }

        var offset = 0;
        for (var i = 0; i <= splitter; i++)
        {
            offset += _widths[i] + (i < splitter ? Gutter : 0);
        }

        return Portrait
            ? new Box(Area.X, Area.Y + offset, Area.Width, Gutter)
            : new Box(Area.X + offset, Area.Y, Gutter, Area.Height);
    }

    public int SplitterAt(double x, double y, int slop)
    {
        for (var i = 0; i < _slots.Count - 1; i++)
        {
            var box = GutterBox(i);
            if (box.IsEmpty)
            {
                continue;
            }

            var grown = new Box(
                box.X - slop, box.Y - slop, box.Width + (slop * 2), box.Height + (slop * 2));
            if (x >= grown.X && y >= grown.Y && x < grown.Right && y < grown.Bottom)
            {
                return i;
            }
        }

        return -1;
    }

    private void EjectUntilFits()
    {
        while (_slots.Count > 1)
        {
            var minimums = 0;
            for (var i = 0; i < _slots.Count; i++)
            {
                minimums += MinimumOf(i);
            }

            if (minimums <= _span)
            {
                return;
            }

            if (_vacant >= 0)
            {
                DropSlot(_vacant);
            }
            else
            {
                Eject(_cells[^1]);
            }

            _span = SpanFor(_slots.Count);
        }
    }

    public bool TrySetSplit(int splitter, int position)
    {
        if (splitter < 0 || splitter >= _widths.Count - 1)
        {
            return false;
        }

        var before = 0;
        for (var i = 0; i < splitter; i++)
        {
            before += _widths[i] + Gutter;
        }

        var pair = _widths[splitter] + _widths[splitter + 1];
        var minFirst = MinimumOf(splitter);
        var minSecond = MinimumOf(splitter + 1);
        if (pair < minFirst + minSecond)
        {
            return false;
        }

        var first = Math.Clamp(position - before, minFirst, pair - minSecond);
        if (first == _widths[splitter])
        {
            return false;
        }

        _widths[splitter] = first;
        _widths[splitter + 1] = pair - first;
        return true;
    }

    public bool TrySplit(T app, int at, double fraction)
    {
        if (_cells.Contains(app))
        {
            return false;
        }

        if (_vacant >= 0)
        {
            Touch(app);
            _slots[_vacant] = app;
            _active = app;
            Sync();
            return true;
        }

        if (!WouldFit(SpanFor(_slots.Count + 1), MinWidthOf(app)))
        {
            return false;
        }

        if (_slots.Count == 0)
        {
            return SplitEmpty(app, at, fraction);
        }

        var index = Math.Clamp(at, 0, _slots.Count);
        var donor = Math.Min(index, _slots.Count - 1);
        Touch(app);
        _slots.Insert(index, app);
        _widths.Insert(Math.Min(index, _widths.Count), 0);
        _active = app;
        Sync();

        var donorIndex = donor >= index ? donor + 1 : donor;
        var available = _widths[donorIndex];
        var floor = MinWidthOf(app);
        var ceiling = available - MinimumOf(donorIndex);
        if (ceiling < floor)
        {
            return true;
        }

        var take = Math.Clamp((int)Math.Round(available * fraction), floor, ceiling);
        _widths[index] = take;
        _widths[donorIndex] = available - take;
        return true;
    }

    private bool SplitEmpty(T app, int at, double fraction)
    {
        Touch(app);
        var leading = at <= 0;
        _slots.Add(leading ? app : null);
        _slots.Add(leading ? null : app);
        _widths.Add(0);
        _widths.Add(0);
        _active = app;
        Sync();

        var span = SpanFor(2);
        var floor = MinWidthOf(app);
        var ceiling = span - MinWidth;
        if (ceiling < floor)
        {
            DropSlot(leading ? 1 : 0);
            return true;
        }

        var take = Math.Clamp((int)Math.Round(span * fraction), floor, ceiling);
        _widths[leading ? 0 : 1] = take;
        _widths[leading ? 1 : 0] = span - take;
        return true;
    }

    private void Release(T app)
    {
        var index = _slots.IndexOf(app);
        if (index < 0)
        {
            return;
        }

        DropSlot(index);
        if (_cells.Count == 0 && _vacant >= 0)
        {
            DropSlot(_vacant);
        }
    }

    private void DropSlot(int index)
    {
        _slots.RemoveAt(index);
        if (index < _widths.Count)
        {
            _widths.RemoveAt(index);
        }

        Sync();
    }

    private void Sync()
    {
        _cells.Clear();
        _vacant = -1;
        for (var i = 0; i < _slots.Count; i++)
        {
            if (_slots[i] is { } app)
            {
                _cells.Add(app);
            }
            else
            {
                _vacant = i;
            }
        }

        while (_widths.Count > _slots.Count)
        {
            _widths.RemoveAt(_widths.Count - 1);
        }

        while (_widths.Count < _slots.Count)
        {
            _widths.Add(0);
        }

        if (_active is not null && !_cells.Contains(_active))
        {
            _active = _cells.Count > 0 ? _cells[^1] : null;
        }
    }

    private int MinWidthOf(T app) => app.MinWidth > 0 ? app.MinWidth : MinWidth;

    private int SpanFor(int slots) =>
        Math.Max(1, (Portrait ? Area.Height : Area.Width) - (Gutter * Math.Max(0, slots - 1)));

    public int MinimumOf(int index) =>
        index >= 0 && index < _slots.Count && _slots[index] is { MinWidth: > 0 } app ? app.MinWidth : MinWidth;

    private void EnsureWidths(int span)
    {
        while (_widths.Count > _slots.Count)
        {
            _widths.RemoveAt(_widths.Count - 1);
        }

        while (_widths.Count < _slots.Count)
        {
            _widths.Add(0);
        }

        if (_slots.Count == 1)
        {
            _widths[0] = span;
            return;
        }

        var total = 0;
        var unset = 0;
        for (var i = 0; i < _widths.Count; i++)
        {
            total += _widths[i];
            if (_widths[i] <= 0)
            {
                unset++;
            }
        }

        if (unset > 0)
        {
            var free = Math.Max(0, span - total);
            var share = free / unset;
            for (var i = 0; i < _widths.Count; i++)
            {
                if (_widths[i] <= 0)
                {
                    _widths[i] = share;
                }
            }
        }

        Normalize(span);
    }

    private void Normalize(int span)
    {
        var count = _widths.Count;
        if (count == 0)
        {
            return;
        }

        var total = 0;
        for (var i = 0; i < count; i++)
        {
            total += _widths[i];
        }

        if (total <= 0)
        {
            var share = span / count;
            for (var i = 0; i < count; i++)
            {
                _widths[i] = share;
            }

            total = share * count;
        }

        for (var i = 0; i < count; i++)
        {
            _widths[i] = Math.Max((int)Math.Round(_widths[i] * (double)span / total), MinimumOf(i));
        }

        var sum = 0;
        for (var i = 0; i < count; i++)
        {
            sum += _widths[i];
        }

        var delta = span - sum;
        while (delta != 0)
        {
            var moved = false;
            for (var i = 0; i < count && delta != 0; i++)
            {
                if (delta > 0)
                {
                    _widths[i]++;
                    delta--;
                    moved = true;
                }
                else if (_widths[i] > MinimumOf(i))
                {
                    _widths[i]--;
                    delta++;
                    moved = true;
                }
            }

            if (!moved)
            {
                return;
            }
        }
    }

    public bool WouldFit(int span, int extra)
    {
        var minimums = extra;
        for (var i = 0; i < _slots.Count; i++)
        {
            minimums += MinimumOf(i);
        }

        return _slots.Count < MaxCells && minimums <= span;
    }

    public bool CanSplit => WouldFit(SpanFor(_slots.Count + 1), MinWidth);
}
