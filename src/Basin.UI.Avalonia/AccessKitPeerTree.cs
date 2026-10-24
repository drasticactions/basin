using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using AccessKit;
using AccessKit.Unix;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Basin.Diagnostics;
using AkAction = AccessKit.Action;
using AvRect = Avalonia.Rect;

namespace Basin.UI.Avalonia;

internal sealed class AccessKitPeerTree : IDisposable
{
    private static readonly NodeId RootId = 1;
    private static readonly TimeSpan MotionInterval = TimeSpan.FromMilliseconds(250);

    private readonly TopLevel _topLevel;
    private readonly string _toolkitVersion;
    private readonly ConditionalWeakTable<AutomationPeer, StrongBox<NodeId>> _ids = new();
    private readonly Dictionary<NodeId, AutomationPeer> _peers = [];
    private readonly Dictionary<NodeId, NodeId[]> _children = [];
    private readonly Dictionary<NodeId, TextRun> _runs = [];
    private readonly HashSet<AutomationPeer> _changedProperties = [];
    private readonly HashSet<AutomationPeer> _changedChildren = [];
    private readonly HashSet<AutomationPeer> _changedMotion = [];
    private readonly AutomationPeer _root;
    private ulong _nextId = 2;
    private bool _active;
    private bool _fullTreeQueued;
    private bool _focusChanged;
    private bool _frameRequested;
    private bool _motionTimerArmed;
    private bool _disposed;

    public AccessKitPeerTree(TopLevel topLevel)
    {
        _topLevel = topLevel;
        _toolkitVersion = typeof(TopLevel).Assembly.GetName().Version?.ToString() ?? "";
        _root = ControlAutomationPeer.CreatePeerForElement(topLevel);
        _ids.Add(_root, new StrongBox<NodeId>(RootId));
        Adapter = new UnixAdapter(RequestInitialTree, DoAction, DeactivateAccessibility, new UnixAdapterOptions { ValidateUpdates = true });
        Adapter.UnhandledException += static (_, error) => BasinLog.For("avalonia").Warn(error, $"accessibility: an adapter handler failed");
        _topLevel.AddHandler(InputElement.GotFocusEvent, OnGotFocus, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    public UnixAdapter Adapter { get; }

    public void SetWindowFocused(bool focused) => Adapter.UpdateWindowFocusState(focused);

    public void SetRootWindowBounds(Rect bounds) => Adapter.SetRootWindowBounds(bounds, bounds);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _topLevel.RemoveHandler(InputElement.GotFocusEvent, OnGotFocus);
        Reset();
        Adapter.Dispose();
    }

    private TreeUpdate? RequestInitialTree()
    {
        Dispatcher.UIThread.Post(SendFullTree, DispatcherPriority.Render);
        var update = new TreeUpdate(RootId) { Tree = new TreeInfo(RootId) { ToolkitName = "Avalonia", ToolkitVersion = _toolkitVersion } };
        update.Push(RootId, new Node(Role.Window));
        update.Focus = RootId;
        return update;
    }

    private void DeactivateAccessibility() => Dispatcher.UIThread.Post(() =>
    {
        _active = false;
        Reset();
    });

    private void DoAction(ActionRequest request) => Dispatcher.UIThread.Post(() => Perform(request));


    private void OnGotFocus(object? sender, FocusChangedEventArgs e)
    {
        _focusChanged = true;
        RequestFrame();
    }

    private void OnPeerChildrenChanged(object? sender, EventArgs e)
    {
        if (sender is AutomationPeer peer)
        {
            _changedChildren.Add(peer);
            RequestFrame();
        }
    }

    private void OnPeerPropertyChanged(object? sender, AutomationPropertyChangedEventArgs e)
    {
        if (sender is not AutomationPeer peer)
        {
            return;
        }

        if (IsMotion(peer, e.Property))
        {
            MarkMotion(peer);
        }
        else
        {
            MarkProperties(peer);
        }
    }

    private void OnOwnerPropertyChanged(object? sender, global::Avalonia.AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is Control owner
            && (e.Property == TextBox.CaretIndexProperty || e.Property == TextBox.SelectionStartProperty || e.Property == TextBox.SelectionEndProperty
                || e.Property == ComboBox.SelectionBoxItemProperty))
        {
            MarkProperties(ControlAutomationPeer.CreatePeerForElement(owner));
        }
    }

    private void MarkProperties(AutomationPeer peer)
    {
        _changedProperties.Add(peer);
        RequestFrame();
    }

    private static bool IsMotion(AutomationPeer peer, AutomationProperty property) =>
        property == AutomationElementIdentifiers.BoundingRectangleProperty
        || property == ScrollPatternIdentifiers.HorizontalScrollPercentProperty
        || property == ScrollPatternIdentifiers.VerticalScrollPercentProperty
        || property == ScrollPatternIdentifiers.HorizontalViewSizeProperty
        || property == ScrollPatternIdentifiers.VerticalViewSizeProperty
        || (property == RangeValuePatternIdentifiers.ValueProperty && peer.GetAutomationControlType() == AutomationControlType.ScrollBar);

    private void MarkMotion(AutomationPeer peer)
    {
        if (!_active || _disposed)
        {
            return;
        }

        _changedMotion.Add(peer);
        if (_motionTimerArmed)
        {
            return;
        }

        _motionTimerArmed = true;
        DispatcherTimer.RunOnce(FlushMotion, MotionInterval, DispatcherPriority.Render);
    }

    private void FlushMotion()
    {
        _motionTimerArmed = false;
        if (!_active || _disposed || _changedMotion.Count == 0)
        {
            _changedMotion.Clear();
            return;
        }

        _changedProperties.UnionWith(_changedMotion);
        _changedMotion.Clear();
        RequestFrame();
    }

    private void RequestFrame()
    {
        if (_frameRequested || !_active || _disposed)
        {
            return;
        }

        _frameRequested = true;
        _topLevel.RequestAnimationFrame(_ => Flush());
    }

    private void SendFullTree()
    {
        if (_disposed)
        {
            return;
        }

        _fullTreeQueued = true;
        _active = true;
        Adapter.UpdateIfActive(() =>
        {
            Reset();
            var update = new TreeUpdate(RootId);
            update.Tree = new TreeInfo(RootId) { ToolkitName = "Avalonia", ToolkitVersion = _toolkitVersion };
            PushSubtree(update, _root, []);
            update.Focus = FocusedId();
            return update;
        });
        _fullTreeQueued = false;
    }

    private void Flush()
    {
        _frameRequested = false;
        if (!_active || _disposed || _fullTreeQueued)
        {
            return;
        }

        if (_changedChildren.Count == 0 && _changedProperties.Count == 0 && !_focusChanged)
        {
            return;
        }

        Adapter.UpdateIfActive(BuildIncrementalUpdate);
        _changedChildren.Clear();
        _changedProperties.Clear();
        _focusChanged = false;
    }

    private TreeUpdate BuildIncrementalUpdate()
    {
        var update = new TreeUpdate(RootId);
        var pushed = new HashSet<NodeId>();
        var changed = new List<(NodeId Id, AutomationPeer Peer, IReadOnlyList<AutomationPeer> Children, NodeId[] Ids)>();
        var kept = new HashSet<NodeId>();
        foreach (var peer in _changedChildren)
        {
            if (TryGetKnownId(peer, out var id))
            {
                var children = peer.GetChildren();
                var ids = children.Select(IdOf).ToArray();
                changed.Add((id, peer, children, ids));
                kept.UnionWith(ids);
            }
        }

        foreach (var (id, _, _, ids) in changed)
        {
            if (!_children.TryGetValue(id, out var previous))
            {
                continue;
            }

            foreach (var removed in previous.Except(ids))
            {
                if (!kept.Contains(removed))
                {
                    Forget(removed);
                }
            }
        }

        foreach (var (id, peer, children, ids) in changed)
        {
            if (!_peers.ContainsKey(id) || pushed.Contains(id))
            {
                continue;
            }

            _children[id] = ids;
            PushNode(update, id, peer, ids, pushed);
            for (var i = 0; i < children.Count; i++)
            {
                if (!_peers.ContainsKey(ids[i]))
                {
                    PushSubtree(update, children[i], pushed);
                }
            }
        }

        foreach (var peer in _changedProperties)
        {
            if (TryGetKnownId(peer, out var id) && !pushed.Contains(id))
            {
                PushNode(update, id, peer, _children[id], pushed);
            }
        }

        update.Focus = FocusedId();
        return update;
    }

    private void PushSubtree(TreeUpdate update, AutomationPeer peer, HashSet<NodeId> pushed)
    {
        var id = IdOf(peer);
        var children = peer.GetChildren();
        var childIds = children.Select(IdOf).ToArray();
        Track(id, peer, childIds);
        PushNode(update, id, peer, childIds, pushed);
        foreach (var child in children)
        {
            if (!pushed.Contains(IdOf(child)))
            {
                PushSubtree(update, child, pushed);
            }
        }
    }

    private void PushNode(TreeUpdate update, NodeId id, AutomationPeer peer, NodeId[] children, HashSet<NodeId> pushed)
    {
        pushed.Add(id);
        var node = BuildNode(peer, children, out var run);
        update.Push(id, node);
        if (run is not null)
        {
            update.Push(run.Id, run.Build());
        }
    }

    private void Track(NodeId id, AutomationPeer peer, NodeId[] children)
    {
        if (_peers.TryAdd(id, peer))
        {
            peer.ChildrenChanged += OnPeerChildrenChanged;
            peer.PropertyChanged += OnPeerPropertyChanged;
            if (peer is ControlAutomationPeer { Owner: TextBox or ComboBox } owned)
            {
                owned.Owner.PropertyChanged += OnOwnerPropertyChanged;
            }
        }

        _children[id] = children;
    }

    private void Untrack(AutomationPeer peer)
    {
        peer.ChildrenChanged -= OnPeerChildrenChanged;
        peer.PropertyChanged -= OnPeerPropertyChanged;
        if (peer is ControlAutomationPeer { Owner: TextBox or ComboBox } owned)
        {
            owned.Owner.PropertyChanged -= OnOwnerPropertyChanged;
        }
    }

    private void Forget(NodeId id)
    {
        if (!_peers.Remove(id, out var peer))
        {
            return;
        }

        Untrack(peer);
        _runs.Remove(id);
        if (_children.Remove(id, out var children))
        {
            foreach (var child in children)
            {
                Forget(child);
            }
        }
    }

    private void Reset()
    {
        foreach (var peer in _peers.Values)
        {
            Untrack(peer);
        }

        _peers.Clear();
        _children.Clear();
        _runs.Clear();
        _changedChildren.Clear();
        _changedProperties.Clear();
        _changedMotion.Clear();
        _focusChanged = false;
    }

    private NodeId IdOf(AutomationPeer peer) => _ids.GetValue(peer, _ => new StrongBox<NodeId>(_nextId++)).Value;

    private bool TryGetKnownId(AutomationPeer peer, out NodeId id)
    {
        if (_ids.TryGetValue(peer, out var box) && _peers.ContainsKey(box.Value))
        {
            id = box.Value;
            return true;
        }

        id = default;
        return false;
    }

    private NodeId FocusedId()
    {
        if (_topLevel.FocusManager?.GetFocusedElement() is Control control)
        {
            var peer = ControlAutomationPeer.CreatePeerForElement(control);
            if (TryGetKnownId(peer, out var id))
            {
                return id;
            }
        }

        return RootId;
    }

    private Node BuildNode(AutomationPeer peer, NodeId[] children, out TextRun? run)
    {
        run = null;
        var owner = (peer as ControlAutomationPeer)?.Owner;
        var box = owner as TextBox;
        var pressable = owner is not null && ChromeAutomation.GetIsButton(owner);
        var role = box is { PasswordChar: not default(char) } ? Role.PasswordInput : pressable ? Role.Button : AccessKitRoles.RoleOf(peer);
        var node = new Node(role);

        var name = peer == _root ? AutomationProperties.GetName(_topLevel) ?? "" : peer.GetName();
        if (IsTypeName(peer, name))
        {
            name = "";
        }

        if (name.Length > 0)
        {
            if (role == Role.Label)
            {
                node.Value = name;
            }
            else
            {
                node.Label = name;
            }
        }

        if (peer.GetHeadingLevel() is > 0 and var level)
        {
            node.Level = level;
        }

        SetIfNotEmpty(peer.GetHelpText(), v => node.Description = v);
        SetIfNotEmpty(peer.GetPlaceholderText(), v => node.Placeholder = v);
        SetIfNotEmpty(peer.GetAutomationId(), v => node.AuthorId = v);
        SetIfNotEmpty(peer.GetClassName(), v => node.ClassName = v);
        SetIfNotEmpty(peer.GetAcceleratorKey(), v => node.KeyboardShortcut = v);
        SetIfNotEmpty(peer.GetAccessKey(), v => node.AccessKey = v);
        node.IsDisabled = !peer.IsEnabled();
        node.IsHidden = peer.IsOffscreen();
        node.Live = peer.GetLiveSetting() switch
        {
            AutomationLiveSetting.Polite => Live.Polite,
            AutomationLiveSetting.Assertive => Live.Assertive,
            _ => null,
        };
        if (peer.GetLabeledBy() is { } label && TryGetKnownId(label, out var labelId))
        {
            node.SetLabelledBy([labelId]);
        }
        else if (name.Length == 0 && role is Role.Tab or Role.ListBoxOption or Role.ListItem or Role.Button or Role.MenuItem or Role.TreeItem or Role.CheckBox or Role.RadioButton or Role.Link)
        {
            var labels = new List<NodeId>();
            CollectLabels(peer, labels, 0);
            if (labels.Count > 0)
            {
                node.SetLabelledBy([.. labels]);
            }
        }

        var bounds = peer.GetBoundingRectangle();
        if (peer == _root)
        {
            bounds = new AvRect(_topLevel.ClientSize);
        }

        node.Bounds = new Rect(bounds.X, bounds.Y, bounds.Right, bounds.Bottom);

        if (peer.IsKeyboardFocusable())
        {
            node.AddAction(AkAction.Focus);
        }

        if (pressable || peer.GetProvider<IInvokeProvider>() is not null)
        {
            node.AddAction(AkAction.Click);
        }

        if (peer.GetProvider<IToggleProvider>() is { } toggle)
        {
            node.Toggled = toggle.ToggleState switch
            {
                ToggleState.On => Toggled.True,
                ToggleState.Off => Toggled.False,
                _ => Toggled.Mixed,
            };
            node.AddAction(AkAction.Click);
        }

        if (peer.GetProvider<IRangeValueProvider>() is { } range)
        {
            node.NumericValue = range.Value;
            node.MinNumericValue = range.Minimum;
            node.MaxNumericValue = range.Maximum;
            node.NumericValueStep = range.SmallChange;
            node.NumericValueJump = range.LargeChange;
            node.IsReadOnly = range.IsReadOnly;
            if (!range.IsReadOnly)
            {
                node.AddAction(AkAction.SetValue);
                node.AddAction(AkAction.Increment);
                node.AddAction(AkAction.Decrement);
            }
        }

        if (peer.GetProvider<IValueProvider>() is { } value)
        {
            var text = value.Value;
            if (role != Role.Label && text is not null)
            {
                node.Value = text;
            }

            node.IsReadOnly = value.IsReadOnly;
            if (!value.IsReadOnly)
            {
                node.AddAction(AkAction.SetValue);
            }

            if (role is Role.TextInput or Role.MultilineTextInput or Role.PasswordInput or Role.SearchInput)
            {
                run = RunOf(peer, text ?? "", bounds, box, Role.TextRun);
                node.AddAction(AkAction.SetTextSelection);
                if (run.Selection is { } selection)
                {
                    node.TextSelection = selection;
                }
            }
        }

        if ((peer as ControlAutomationPeer)?.Owner is ComboBox combo && ShownText(combo.SelectionBoxItem) is { Length: > 0 } shown)
        {
            node.Value = shown;
            run = RunOf(peer, shown, bounds, null, Role.ListBoxOption);
        }

        node.SetChildren(run is null ? children : [.. children, run.Id]);

        if (peer.GetProvider<IExpandCollapseProvider>() is { } expand)
        {
            switch (expand.ExpandCollapseState)
            {
                case ExpandCollapseState.Collapsed:
                    node.Expanded = false;
                    node.AddAction(AkAction.Expand);
                    break;
                case ExpandCollapseState.Expanded:
                case ExpandCollapseState.PartiallyExpanded:
                    node.Expanded = true;
                    node.AddAction(AkAction.Collapse);
                    break;
            }
        }

        if (peer.GetProvider<ISelectionItemProvider>() is { } selected)
        {
            node.Selected = selected.IsSelected;
            node.AddAction(AkAction.Click);
        }

        if (peer.GetProvider<IScrollProvider>() is { } scroll)
        {
            if (scroll.HorizontallyScrollable)
            {
                node.ScrollX = scroll.HorizontalScrollPercent;
                node.ScrollXMin = 0;
                node.ScrollXMax = 100;
                node.AddAction(AkAction.ScrollLeft);
                node.AddAction(AkAction.ScrollRight);
            }

            if (scroll.VerticallyScrollable)
            {
                node.ScrollY = scroll.VerticalScrollPercent;
                node.ScrollYMin = 0;
                node.ScrollYMax = 100;
                node.AddAction(AkAction.ScrollUp);
                node.AddAction(AkAction.ScrollDown);
            }
        }

        if (peer != _root)
        {
            node.AddAction(AkAction.ScrollIntoView);
        }

        return node;
    }

    private TextRun RunOf(AutomationPeer peer, string text, AvRect bounds, TextBox? box, Role role)
    {
        var owner = IdOf(peer);
        if (!_runs.TryGetValue(owner, out var run) || run.Role != role)
        {
            run = new TextRun(_nextId++, role);
            _runs[owner] = run;
        }

        run.Set(text, new Rect(bounds.X, bounds.Y, bounds.Right, bounds.Bottom));
        run.Selection = null;
        if (box is not null)
        {
            var caret = box.CaretIndex;
            var anchor = box.SelectionStart == box.SelectionEnd ? caret : box.SelectionStart == caret ? box.SelectionEnd : box.SelectionStart;
            run.Selection = new TextSelection(new TextPosition(run.Id, run.CharacterIndexOf(anchor)), new TextPosition(run.Id, run.CharacterIndexOf(caret)));
        }

        return run;
    }

    private void CollectLabels(AutomationPeer peer, List<NodeId> labels, int depth)
    {
        if (depth > 16)
        {
            return;
        }

        foreach (var child in peer.GetChildren())
        {
            var type = child.GetAutomationControlType();
            if (type == AutomationControlType.Text && child.GetName() is { Length: > 0 } text && !IsTypeName(child, text))
            {
                labels.Add(IdOf(child));
            }
            else if (type is AutomationControlType.None or AutomationControlType.Group or AutomationControlType.Custom)
            {
                CollectLabels(child, labels, depth + 1);
            }
        }
    }

    private static string? ShownText(object? item) => item switch
    {
        null => null,
        string text => text,
        ContentControl { Content: string text } => text,
        TextBlock block => block.Text,
        _ => item.GetType().FullName == item.ToString() ? null : item.ToString(),
    };

    private static bool IsTypeName(AutomationPeer peer, string name)
    {
        if (name.Length == 0 || peer is not ControlAutomationPeer { Owner: var owner })
        {
            return false;
        }

        return IsTypeOf(owner.DataContext, name)
            || (owner is ContentControl content && IsTypeOf(content.Content, name))
            || (owner is global::Avalonia.Controls.Primitives.HeaderedContentControl headered && IsTypeOf(headered.Header, name))
            || (owner is TextBlock block && IsTypeOf(block.DataContext, name));
    }

    private static bool IsTypeOf(object? value, string name) => value is not null and not string && name == value.GetType().FullName;

    private static void SetIfNotEmpty(string? value, Action<string> set)
    {
        if (!string.IsNullOrEmpty(value))
        {
            set(value);
        }
    }

    private void Perform(ActionRequest request)
    {
        if (_disposed || !request.TargetTree.IsRoot || !_peers.TryGetValue(request.TargetNode, out var peer))
        {
            return;
        }

        switch (request.Action)
        {
            case AkAction.Click:
                if (peer is ControlAutomationPeer { Owner: { } pressed } && ChromeAutomation.GetIsButton(pressed))
                {
                    ChromeAutomation.Invoke(pressed);
                }
                else if (peer.GetProvider<IInvokeProvider>() is { } invoke)
                {
                    invoke.Invoke();
                }
                else if (peer.GetProvider<IToggleProvider>() is { } toggle)
                {
                    toggle.Toggle();
                }
                else if (peer.GetProvider<ISelectionItemProvider>() is { } item)
                {
                    item.Select();
                }

                break;
            case AkAction.Focus:
                peer.SetFocus();
                break;
            case AkAction.SetValue when request.Data is ActionData.Value v && peer.GetProvider<IValueProvider>() is { } value:
                value.SetValue(v.Text);
                break;
            case AkAction.SetValue when request.Data is ActionData.NumericValue n && peer.GetProvider<IRangeValueProvider>() is { } range:
                range.SetValue(Math.Clamp(n.Number, range.Minimum, range.Maximum));
                break;
            case AkAction.SetTextSelection when request.Data is ActionData.SetTextSelection s && peer is ControlAutomationPeer { Owner: TextBox box } && _runs.TryGetValue(request.TargetNode, out var run):
                box.SelectionStart = run.Utf16IndexOf(s.Selection.Anchor.CharacterIndex);
                box.SelectionEnd = run.Utf16IndexOf(s.Selection.Focus.CharacterIndex);
                box.CaretIndex = box.SelectionEnd;
                break;
            case AkAction.Increment when peer.GetProvider<IRangeValueProvider>() is { } range:
                range.SetValue(Math.Min(range.Maximum, range.Value + range.SmallChange));
                break;
            case AkAction.Decrement when peer.GetProvider<IRangeValueProvider>() is { } range:
                range.SetValue(Math.Max(range.Minimum, range.Value - range.SmallChange));
                break;
            case AkAction.Expand when peer.GetProvider<IExpandCollapseProvider>() is { } expand:
                expand.Expand();
                break;
            case AkAction.Collapse when peer.GetProvider<IExpandCollapseProvider>() is { } expand:
                expand.Collapse();
                break;
            case AkAction.ScrollIntoView:
                peer.BringIntoView();
                break;
            case AkAction.ShowContextMenu:
                peer.ShowContextMenu();
                break;
            case AkAction.ScrollUp when peer.GetProvider<IScrollProvider>() is { } scroll:
                scroll.Scroll(ScrollAmount.NoAmount, ScrollAmount.SmallDecrement);
                break;
            case AkAction.ScrollDown when peer.GetProvider<IScrollProvider>() is { } scroll:
                scroll.Scroll(ScrollAmount.NoAmount, ScrollAmount.SmallIncrement);
                break;
            case AkAction.ScrollLeft when peer.GetProvider<IScrollProvider>() is { } scroll:
                scroll.Scroll(ScrollAmount.SmallDecrement, ScrollAmount.NoAmount);
                break;
            case AkAction.ScrollRight when peer.GetProvider<IScrollProvider>() is { } scroll:
                scroll.Scroll(ScrollAmount.SmallIncrement, ScrollAmount.NoAmount);
                break;
        }

        _changedProperties.Add(peer);
        RequestFrame();
    }

    private sealed class TextRun(NodeId id, Role role)
    {
        private int[] _starts = [0];
        private byte[] _lengths = [];
        private string? _text;
        private Rect _bounds;

        public NodeId Id { get; } = id;

        public Role Role { get; } = role;

        public TextSelection? Selection { get; set; }

        public void Set(string text, Rect bounds)
        {
            _bounds = bounds;
            if (text == _text)
            {
                return;
            }

            _text = text;
            var starts = new List<int>();
            var lengths = new List<byte>();
            var elements = StringInfo.GetTextElementEnumerator(text);
            while (elements.MoveNext())
            {
                starts.Add(elements.ElementIndex);
                lengths.Add((byte)Math.Min(byte.MaxValue, Encoding.UTF8.GetByteCount(elements.GetTextElement())));
            }

            starts.Add(text.Length);
            _starts = [.. starts];
            _lengths = [.. lengths];
        }

        public int CharacterIndexOf(int utf16)
        {
            var index = Array.BinarySearch(_starts, Math.Clamp(utf16, 0, _starts[^1]));
            return index >= 0 ? index : ~index - 1;
        }

        public int Utf16IndexOf(int character) => _starts[Math.Clamp(character, 0, _starts.Length - 1)];

        public Node Build()
        {
            var node = new Node(Role) { Bounds = _bounds };
            if (Role == Role.TextRun)
            {
                node.Value = _text ?? "";
                node.SetCharacterLengths(_lengths);
            }
            else
            {
                node.Label = _text ?? "";
                node.Selected = true;
            }

            return node;
        }
    }
}
