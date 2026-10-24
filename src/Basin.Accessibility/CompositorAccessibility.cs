using AccessKit;
using AccessKit.Unix;
using Basin.Capabilities;
using Basin.Diagnostics;
using static Basin.Accessibility.AccessibilityLog;
using AkAction = AccessKit.Action;

namespace Basin.Accessibility;

/// <summary>
/// The compositor's own window on the accessibility bus. It speaks announcements through a live label, and it holds the nodes of every
/// registered <see cref="IAccessibleChrome"/>, such as server-side frames and immediate-mode panels.
/// All state belongs to the compositor loop. AccessKit's handlers post to it.
/// </summary>
public sealed class CompositorAccessibility : IAnnouncer, IDisposable
{
    private const ulong RootId = 1;
    private const ulong AnnouncementId = 2;
    private const ulong FirstChromeId = 16;

    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly ICompositorEventLoop _loop;
    private readonly CompositorSynchronizationContext _context;
    private readonly string _name;
    private readonly List<IAccessibleChrome> _chrome = [];
    private readonly AccessibleTreeBuilder _builder = new();
    private readonly List<System.Action?> _presses = [];
    private readonly UnixAdapter _adapter;
    private string _announcement = "";
    private Live _live = Live.Polite;
    private bool _active;
    private bool _rebuildQueued;
    private bool _disposed;

    private CompositorAccessibility(ICompositorEventLoop loop, string name)
    {
        _loop = loop;
        _name = name;
        _context = new CompositorSynchronizationContext(loop);
        _adapter = new UnixAdapter(RequestInitialTree, DoAction, Deactivate, new UnixAdapterOptions { ValidateUpdates = true });
        _adapter.UnhandledException += static (_, error) => Log.Warn(error, $"tree: an adapter handler failed");
        _adapter.SetRootWindowBounds(new Rect(0, 0, 0, 0), new Rect(0, 0, 0, 0));
        BasinCounters.Track();
    }

    /// <summary>
    /// Gets the number of registered chrome providers.
    /// </summary>
    public int ChromeCount => _chrome.Count;

    /// <summary>
    /// Gets the last announcement, without the space that marks a repeat.
    /// </summary>
    public string LastAnnouncement => _announcement.TrimEnd(' ');

    /// <summary>
    /// Makes the compositor's window. Call it on the loop thread.
    /// </summary>
    /// <param name="loop">The compositor loop.</param>
    /// <param name="name">The window's name, usually the compositor's name.</param>
    /// <param name="reason">The reason when the method returns <see langword="null"/>, otherwise <see langword="null"/>.</param>
    /// <returns>The window, which must be disposed, or <see langword="null"/> when libaccesskit does not load.</returns>
    public static CompositorAccessibility? TryStart(ICompositorEventLoop loop, string name, out string? reason)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(name);
        try
        {
            reason = null;
            return new CompositorAccessibility(loop, name);
        }
        catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException or TypeInitializationException or BadImageFormatException)
        {
            reason = $"libaccesskit is not loadable ({error.Message})";
            Log.Info($"tree: not started, {reason}");
            return null;
        }
    }

    /// <summary>
    /// Adds chrome to the window. Its nodes appear after the next loop idle.
    /// </summary>
    /// <param name="chrome">The chrome.</param>
    public void Add(IAccessibleChrome chrome)
    {
        _thread.Assert();
        ArgumentNullException.ThrowIfNull(chrome);
        if (_disposed || _chrome.Contains(chrome))
        {
            return;
        }

        _chrome.Add(chrome);
        chrome.Changed += QueueRebuild;
        QueueRebuild();
    }

    /// <summary>
    /// Takes chrome off the window. A second call does nothing.
    /// </summary>
    /// <param name="chrome">The chrome.</param>
    public void Remove(IAccessibleChrome chrome)
    {
        _thread.Assert();
        if (_chrome.Remove(chrome))
        {
            chrome.Changed -= QueueRebuild;
            QueueRebuild();
        }
    }

    /// <inheritdoc/>
    public void Announce(string text, AnnouncePriority priority = AnnouncePriority.Polite)
    {
        _thread.Assert();
        if (_disposed || string.IsNullOrEmpty(text))
        {
            return;
        }

        _announcement = text == _announcement ? text + " " : text;
        _live = priority == AnnouncePriority.Assertive ? Live.Assertive : Live.Polite;
        if (_active)
        {
            _adapter.UpdateIfActive(() =>
            {
                var update = new TreeUpdate(RootId);
                update.Push(AnnouncementId, AnnouncementNode());
                update.Focus = RootId;
                return update;
            });
        }
    }

    /// <summary>
    /// Takes the window off the bus. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _thread.Assert();
        _disposed = true;
        foreach (var chrome in _chrome)
        {
            chrome.Changed -= QueueRebuild;
        }

        _chrome.Clear();
        _adapter.Dispose();
        _context.Dispose();
        BasinCounters.Untrack();
    }

    private TreeUpdate? RequestInitialTree()
    {
        _context.Post(_ => SendFullTree(), null);
        var update = new TreeUpdate(RootId) { Tree = new TreeInfo(RootId) { ToolkitName = "basin" } };
        update.Push(RootId, new Node(Role.Window) { Label = _name });
        update.Focus = RootId;
        return update;
    }

    private void Deactivate() => _context.Post(_ => _active = false, null);

    private void DoAction(ActionRequest request) => _context.Post(_ => Perform(request), null);

    private void Perform(ActionRequest request)
    {
        if (_disposed || request.Action != AkAction.Click)
        {
            return;
        }

        var index = (long)(ulong)request.TargetNode - (long)FirstChromeId;
        if (index >= 0 && index < _presses.Count && _presses[(int)index] is { } press)
        {
            try
            {
                press();
            }
            catch (Exception error)
            {
                Log.Warn(error, $"tree: a press failed");
            }
        }
    }

    private void QueueRebuild()
    {
        if (_rebuildQueued || _disposed)
        {
            return;
        }

        _rebuildQueued = true;
        _loop.AddIdle(() =>
        {
            _rebuildQueued = false;
            if (_active && !_disposed)
            {
                SendTree(full: false);
            }
        });
    }

    private void SendFullTree()
    {
        if (_disposed)
        {
            return;
        }

        _active = true;
        SendTree(full: true);
    }

    private void SendTree(bool full)
    {
        _builder.Clear();
        foreach (var chrome in _chrome)
        {
            try
            {
                chrome.Describe(_builder);
            }
            catch (Exception error)
            {
                Log.Warn(error, $"tree: {chrome.GetType().Name} did not describe itself");
            }
        }

        _presses.Clear();
        for (var i = 0; i < _builder.Count; i++)
        {
            _presses.Add(_builder.NodeAt(i).Press);
        }

        _adapter.UpdateIfActive(() =>
        {
            var update = new TreeUpdate(RootId);
            if (full)
            {
                update.Tree = new TreeInfo(RootId) { ToolkitName = "basin", ToolkitVersion = typeof(CompositorAccessibility).Assembly.GetName().Version?.ToString() ?? "" };
            }

            var children = new List<List<NodeId>>(_builder.Count);
            var topLevel = new List<NodeId> { AnnouncementId };
            for (var i = 0; i < _builder.Count; i++)
            {
                children.Add([]);
                var parent = _builder.ParentOf(i);
                if (parent < 0)
                {
                    topLevel.Add(IdOf(i));
                }
                else
                {
                    children[parent].Add(IdOf(i));
                }
            }

            var root = new Node(Role.Window) { Label = _name };
            root.SetChildren(topLevel.ToArray());
            update.Push(RootId, root);
            update.Push(AnnouncementId, AnnouncementNode());
            var extra = FirstChromeId + (ulong)_builder.Count;
            for (var i = 0; i < _builder.Count; i++)
            {
                var described = _builder.NodeAt(i);
                if (ExtraChild(described) is { } child)
                {
                    children[i].Add(extra);
                    update.Push(extra++, child);
                }

                update.Push(IdOf(i), Build(described, children[i]));
            }

            update.Focus = RootId;
            return update;
        });
    }

    private static NodeId IdOf(int index) => FirstChromeId + (ulong)index;

    private static Node? ExtraChild(in AccessibleNode described)
    {
        if (described.Value is not { } value)
        {
            return null;
        }

        var box = described.Bounds;
        var bounds = new Rect(box.X, box.Y, box.X + box.Width, box.Y + box.Height);
        switch (described.Role)
        {
            case AccessibleRole.ComboBox:
                return new Node(Role.ListBoxOption) { Label = value, Selected = true, Bounds = bounds };
            case AccessibleRole.TextInput:
            {
                var run = new Node(Role.TextRun) { Value = value, Bounds = bounds };
                var lengths = new List<byte>();
                var elements = System.Globalization.StringInfo.GetTextElementEnumerator(value);
                while (elements.MoveNext())
                {
                    lengths.Add((byte)Math.Min(byte.MaxValue, System.Text.Encoding.UTF8.GetByteCount(elements.GetTextElement())));
                }

                run.SetCharacterLengths(lengths.ToArray());
                return run;
            }

            default:
                return null;
        }
    }

    private Node AnnouncementNode() => new(Role.Label) { Value = _announcement, Live = _live };

    private static Node Build(in AccessibleNode described, List<NodeId> children)
    {
        var node = new Node(RoleOf(described.Role));
        if (described.Name is { Length: > 0 } name)
        {
            if (described.Role == AccessibleRole.Label)
            {
                node.Value = name;
            }
            else
            {
                node.Label = name;
            }
        }

        if (described.Value is { } value && described.Role != AccessibleRole.Label)
        {
            node.Value = value;
            if (described.Role == AccessibleRole.Slider && double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number))
            {
                node.NumericValue = number;
            }
        }

        if (described.Role == AccessibleRole.Heading)
        {
            node.Level = 2;
        }

        var box = described.Bounds;
        node.Bounds = new Rect(box.X, box.Y, box.X + box.Width, box.Y + box.Height);
        if (described.Role == AccessibleRole.CheckBox)
        {
            node.Toggled = described.States.HasFlag(AccessibleStates.Checked) ? Toggled.True : Toggled.False;
        }

        if (described.States.HasFlag(AccessibleStates.Selected))
        {
            node.Selected = true;
        }

        node.IsDisabled = described.States.HasFlag(AccessibleStates.Disabled);
        if (described.Press is not null)
        {
            node.AddAction(AkAction.Click);
        }

        node.SetChildren(children.ToArray());
        return node;
    }

    private static Role RoleOf(AccessibleRole role) => role switch
    {
        AccessibleRole.Window => Role.Window,
        AccessibleRole.Group => Role.Group,
        AccessibleRole.Button => Role.Button,
        AccessibleRole.Label => Role.Label,
        AccessibleRole.Heading => Role.Heading,
        AccessibleRole.CheckBox => Role.CheckBox,
        AccessibleRole.ComboBox => Role.ComboBox,
        AccessibleRole.Slider => Role.Slider,
        AccessibleRole.TextInput => Role.TextInput,
        AccessibleRole.List => Role.ListBox,
        AccessibleRole.ListItem => Role.ListBoxOption,
        AccessibleRole.MenuItem => Role.MenuItem,
        AccessibleRole.Image => Role.Image,
        _ => Role.GenericContainer,
    };
}
