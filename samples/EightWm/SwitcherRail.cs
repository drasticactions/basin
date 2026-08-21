using Basin;
using Basin.Scene;

namespace EightWm;

internal sealed class SwitcherRail : IDisposable
{
    public const int RailWidth = 168;
    public const int EntryHeight = 108;
    public const int EntryGap = 8;
    public const int Margin = 8;

    private static readonly RenderColor Backing = new(0.10f, 0.10f, 0.10f, 0.92f);
    private static readonly RenderColor Slot = new(0.18f, 0.18f, 0.18f, 1f);

    private readonly SceneTree _root;
    private readonly SceneRect _backing;
    private readonly List<Entry> _entries = [];
    private readonly List<AppWindow> _shown = [];

    public SwitcherRail(SceneTransform parent)
    {
        _root = new SceneTree(parent);
        _backing = new SceneRect(_root, 1, 1, Backing);
    }

    private sealed class Entry
    {
        public required SceneTree Slot { get; init; }

        public required SceneRect Backing { get; init; }

        public required SceneTransform Frame { get; init; }

        public SceneMirror? Mirror { get; set; }

        public Box Box { get; set; }

        public AppWindow? App { get; set; }
    }

    public bool Enabled
    {
        get => _root.Enabled;
        set => _root.Enabled = value;
    }

    public int Count => _shown.Count;

    public double Scale { get; set; } = 1;

    public Box Box { get; private set; }

    public AppWindow? EntryAt(double x, double y)
    {
        for (var i = 0; i < _entries.Count && i < _shown.Count; i++)
        {
            var box = _entries[i].Box;
            if (x >= box.X && y >= box.Y && x < box.Right && y < box.Bottom)
            {
                return _entries[i].App;
            }
        }

        return null;
    }

    public void Rebuild(IReadOnlyList<AppWindow> apps, int outputHeight, double scale)
    {
        Scale = scale;
        _shown.Clear();
        foreach (var app in apps)
        {
            if (!app.Slot.IsDestroyed && !app.Closing)
            {
                _shown.Add(app);
            }
        }

        const int width = RailWidth;
        const int entryHeight = EntryHeight;
        const int gap = EntryGap;
        const int margin = Margin;
        Box = new Box(0, 0, width, outputHeight);
        _backing.Width = width;
        _backing.Height = outputHeight;

        while (_entries.Count < _shown.Count)
        {
            var slot = new SceneTree(_root);
            _entries.Add(new Entry
            {
                Slot = slot,
                Backing = new SceneRect(slot, 1, 1, Slot),
                Frame = new SceneTransform(slot),
            });
        }

        for (var i = 0; i < _entries.Count; i++)
        {
            var entry = _entries[i];
            if (i >= _shown.Count)
            {
                entry.Slot.Enabled = false;
                entry.App = null;
                DropMirror(entry);
                continue;
            }

            var app = _shown[i];
            var box = new Box(margin, margin + (i * (entryHeight + gap)), width - (margin * 2), entryHeight);
            entry.Box = box;
            entry.App = app;
            entry.Slot.Enabled = box.Bottom <= outputHeight;
            entry.Slot.SetPosition(box.X, box.Y);
            entry.Slot.ClipBox = new Box(0, 0, box.Width, box.Height);
            entry.Backing.Width = box.Width;
            entry.Backing.Height = box.Height;

            var cell = app.Cell;
            var contentWidth = cell.Width > 0 ? cell.Width : box.Width;
            var contentHeight = cell.Height > 0 ? cell.Height : box.Height;
            var factor = Math.Min(box.Width / (double)contentWidth, box.Height / (double)contentHeight);

            if (entry.Mirror is null || !ReferenceEquals(entry.Mirror.Source, app.Slot))
            {
                DropMirror(entry);
                entry.Mirror = new SceneMirror(entry.Frame, app.Slot, contentWidth, contentHeight);
            }
            else
            {
                entry.Mirror.Width = contentWidth;
                entry.Mirror.Height = contentHeight;
            }

            entry.Frame.SetPosition(
                (int)Math.Round((box.Width - (contentWidth * factor)) / 2),
                (int)Math.Round((box.Height - (contentHeight * factor)) / 2));
            entry.Frame.Matrix = RenderTransform.Scale(factor, factor);
        }
    }

    public void Forget(AppWindow app)
    {
        foreach (var entry in _entries)
        {
            if (ReferenceEquals(entry.App, app))
            {
                entry.App = null;
                entry.Slot.Enabled = false;
                DropMirror(entry);
            }
        }

        _shown.Remove(app);
    }

    private static void DropMirror(Entry entry)
    {
        if (entry.Mirror is { IsDestroyed: false } mirror)
        {
            mirror.Destroy();
        }

        entry.Mirror = null;
    }

    public void Dispose()
    {
        foreach (var entry in _entries)
        {
            DropMirror(entry);
        }

        _entries.Clear();
        _shown.Clear();
        if (!_root.IsDestroyed)
        {
            _root.Destroy();
        }
    }
}
