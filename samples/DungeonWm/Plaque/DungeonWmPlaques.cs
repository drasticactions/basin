using Basin;
using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Render.Gl;
using Basin.Render.Vulkan;
using Basin.Scene;
using Basin.UI.Quill;
using Prowl.Scribe;

namespace DungeonWm;

internal sealed class DungeonWmPlaques : IDisposable
{
    private readonly IRenderer _renderer;
    private readonly SceneTree _tree;
    private readonly Dictionary<object, Entry> _entries = [];
    private readonly PlaquePainter _painter;
    private readonly FontFile _font;
    private readonly IUIHost? _host;

    public DungeonWmPlaques(IRenderer renderer, IAllocator? allocator, SceneTree root, BasinLogger log)
    {
        _renderer = renderer;
        _tree = new SceneTree(root) { Enabled = false };
        using (var stream = typeof(DungeonWmPlaques).Assembly.GetManifestResourceStream("Selawik-Semibold.ttf")
            ?? throw new InvalidOperationException("The font Selawik-Semibold.ttf is not embedded in the program."))
        {
            _font = new FontFile(stream);
        }

        _painter = new PlaquePainter(_font);
        _host = Basin.Renderers.QuillHosts.For(renderer, allocator, null, out var declined, out var vulkanDeclined);
        if (vulkanDeclined is not null)
        {
            log.Warn($"plaques: cannot draw on the Vulkan device, trying a GL device: {vulkanDeclined}");
        }

        Declined = declined;
        if (_host is null)
        {
            log.Warn($"plaques: drawing without text: {declined}");
        }
    }

    public bool Available => _host is not null;

    public string? Declined { get; }

    public int Redrawn { get; private set; }

    public void Sync(DungeonWmWindow window, bool focused)
    {
        var frame = window.World.ShownFrame;
        if (frame is not { HasPlaque: true })
        {
            if (frame is not null)
            {
                frame.PlaqueTexture = null;
            }

            return;
        }

        if (_host is null)
        {
            frame.PlaqueTexture = null;
            return;
        }

        if (!_entries.TryGetValue(window, out var entry))
        {
            entry = new Entry(new UISurfaceNode(_tree, _host));
            _entries[window] = entry;
        }

        var title = window.Title.Length > 0 ? window.Title : window.AppId;
        var state = new PlaqueState(title, focused, window.PlaqueHover, window.PlaquePressed, frame.PlaquePixelWidth, frame.PlaquePixelHeight);
        if (state != entry.Drawn && !entry.Node.IsFaulted && Draw(entry, state))
        {
            entry.Drawn = state;
            Redrawn++;
        }

        var node = entry.Node.Node;
        frame.PlaqueTexture = node.TextureFor(_renderer);
        frame.PlaqueSource = node.SourceBox;
    }

    public void Forget(object owner)
    {
        if (_entries.Remove(owner, out var entry))
        {
            entry.Node.Dispose();
        }

        if (owner is DungeonWmWindow { World.Frame: { } frame })
        {
            frame.PlaqueTexture = null;
        }
    }

    public void Transfer(DungeonWmWindow window, object owner)
    {
        if (_entries.Remove(window, out var entry))
        {
            _entries[owner] = entry;
        }
    }

    public void Dispose()
    {
        foreach (var entry in _entries.Values)
        {
            entry.Node.Dispose();
        }

        _entries.Clear();
        _host?.Dispose();
        _tree.Destroy();
    }

    private bool Draw(Entry entry, in PlaqueState state)
    {
        if (!entry.Node.Configure(state.Width, state.Height, 1) || entry.Node.Surface is not IQuillUISurface quill)
        {
            return false;
        }

        var canvas = quill.BeginDraw();
        try
        {
            if (!entry.Prepared)
            {
                QuillFonts.AddFallbacks(canvas, _font);
                entry.Prepared = true;
            }

            _painter.Paint(canvas, state);
        }
        finally
        {
            quill.EndDraw();
        }

        return entry.Node.Publish();
    }

    private sealed class Entry(UISurfaceNode node)
    {
        public UISurfaceNode Node { get; } = node;

        public PlaqueState Drawn { get; set; }

        public bool Prepared { get; set; }
    }
}
