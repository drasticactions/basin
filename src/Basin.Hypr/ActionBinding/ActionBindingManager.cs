using Basin.Capabilities;
using Basin.Config;
using Basin.Hypr.Protocol;
using Wayland.Server;
using static Basin.Hypr.HyprLog;

namespace Basin.Hypr;

/// <summary>
/// Advertises <c>gamescope_action_binding_manager</c>. It registers each armed keyboard trigger as a shortcut in an <see cref="IGlobalShortcuts"/> registry and sends <c>triggered</c> when the shortcut fires.
/// </summary>
public sealed class ActionBindingManager : IGlobalShortcutObserver, IDisposable
{
    /// <summary>
    /// The highest protocol version that the global advertises.
    /// </summary>
    public const int Version = 1;

    private const string AppIdPrefix = "gamescope-action-binding";

    private static readonly (uint Keysym, Modifiers Modifier)[] ModifierKeysyms =
    [
        (0xffe1, Modifiers.Shift), (0xffe2, Modifiers.Shift),
        (0xffe3, Modifiers.Ctrl), (0xffe4, Modifiers.Ctrl),
        (0xffe9, Modifiers.Alt), (0xffea, Modifiers.Alt),
        (0xffe7, Modifiers.Super), (0xffe8, Modifiers.Super),
        (0xffeb, Modifiers.Super), (0xffec, Modifiers.Super),
    ];

    private readonly WlGlobal _global;
    private readonly IGlobalShortcuts? _registry;
    private readonly IActionBindingPolicy _policy;
    private readonly Dictionary<(string AppId, string Id), Binding> _byShortcut = [];
    private uint _sequence;
    private ulong _nextBinding;

    /// <summary>
    /// Initializes a new instance of the <see cref="ActionBindingManager"/> class and subscribes to <paramref name="registry"/>.
    /// </summary>
    /// <param name="display">The display that hosts the global.</param>
    /// <param name="registry">The shortcut registry. When it is <see langword="null"/>, bindings can arm but never fire.</param>
    /// <param name="policy">The policy that decides which clients can register triggers. <see langword="null"/> selects <see cref="SandboxActionBindingPolicy"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="display"/> is <see langword="null"/>.</exception>
    public ActionBindingManager(WlServerDisplay display, IGlobalShortcuts? registry, IActionBindingPolicy? policy)
    {
        ArgumentNullException.ThrowIfNull(display);
        _registry = registry;
        _policy = policy ?? new SandboxActionBindingPolicy();
        _registry?.AddObserver(this);
        _global = display.CreateGlobal(Protocol.GamescopeActionBindingManager.Interface, Version, OnBind);
    }

    /// <summary>
    /// Gets the number of triggers that are registered in the shortcut registry now, over all bindings.
    /// </summary>
    public int Armed => _byShortcut.Count;

    /// <summary>
    /// Converts a list of keysyms into one key and a modifier mask. An uppercase Latin letter becomes lowercase. A list of only modifiers uses the last modifier as the key.
    /// </summary>
    /// <param name="keysyms">The keysyms of one trigger, in any order.</param>
    /// <param name="keysym">The non-modifier keysym, or the last modifier keysym when the list has only modifiers.</param>
    /// <param name="modifiers">The modifiers that must be held with <paramref name="keysym"/>.</param>
    /// <returns><see langword="false"/> when the list is empty or holds more than one non-modifier key.</returns>
    public static bool TryChord(ReadOnlySpan<uint> keysyms, out uint keysym, out Modifiers modifiers)
    {
        keysym = 0;
        modifiers = Modifiers.None;
        uint lastModifier = 0;
        var keys = 0;
        foreach (var raw in keysyms)
        {
            var modifier = ModifierOf(raw);
            if (modifier != Modifiers.None)
            {
                modifiers |= modifier;
                lastModifier = raw;
                continue;
            }

            keys++;
            keysym = raw is >= 0x41 and <= 0x5a ? raw + 0x20 : raw;
        }

        if (keys > 1)
        {
            return false;
        }

        if (keys == 0)
        {
            if (lastModifier == 0)
            {
                return false;
            }

            keysym = lastModifier;
            modifiers &= ~ModifierOf(lastModifier);
        }

        return true;
    }

    /// <summary>
    /// Does nothing. The manager tracks only the shortcuts that it registered itself.
    /// </summary>
    /// <param name="shortcut">The shortcut that was added. It is ignored.</param>
    public void ShortcutRegistered(in GlobalShortcutInfo shortcut)
    {
    }

    /// <summary>
    /// Forgets the binding of a removed shortcut, so that a later activation of the same id does not fire it.
    /// </summary>
    /// <param name="shortcut">The shortcut that was removed.</param>
    public void ShortcutRemoved(in GlobalShortcutInfo shortcut) => _byShortcut.Remove((shortcut.AppId, shortcut.Id));

    /// <summary>
    /// Sends <c>triggered</c> to the binding that owns the shortcut, with the current monotonic time. A one-shot binding then disarms its other triggers.
    /// </summary>
    /// <param name="shortcut">The shortcut that was pressed. A shortcut that no binding owns is ignored.</param>
    /// <param name="timestampMs">The time of the press. The event uses the current clock instead.</param>
    public void ShortcutActivated(in GlobalShortcutInfo shortcut, ulong timestampMs)
    {
        if (!_byShortcut.TryGetValue((shortcut.AppId, shortcut.Id), out var binding))
        {
            return;
        }

        var nanos = (ulong)MonotonicClock.Nanos;
        binding.Resource.SendTriggered(_sequence++, (uint)nanos, (uint)(nanos >> 32), GamescopeActionBinding.TriggerFlag.Keyboard);
        if ((binding.Flags & GlobalShortcutFlags.OneShot) != 0)
        {
            binding.Disarm(except: shortcut.Id);
        }
    }

    /// <summary>
    /// Does nothing. The protocol has no release event.
    /// </summary>
    /// <param name="shortcut">The shortcut that was released. It is ignored.</param>
    /// <param name="timestampMs">The time of the release. It is ignored.</param>
    public void ShortcutDeactivated(in GlobalShortcutInfo shortcut, ulong timestampMs)
    {
    }

    /// <summary>
    /// Disarms every binding, which removes its shortcuts from the registry, then unsubscribes from the registry and destroys the global.
    /// </summary>
    public void Dispose()
    {
        foreach (var binding in _byShortcut.Values.Distinct().ToArray())
        {
            binding.Disarm(except: null);
        }

        _registry?.RemoveObserver(this);
        _global.Dispose();
    }

    private static Modifiers ModifierOf(uint keysym)
    {
        foreach (var (candidate, modifier) in ModifierKeysyms)
        {
            if (candidate == keysym)
            {
                return modifier;
            }
        }

        return Modifiers.None;
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new GamescopeActionBindingManagerResource(client, version, id);
        var allowed = _policy.Allows(client);
        manager.CreateActionBinding += (_, e) =>
        {
            var resource = new GamescopeActionBindingResource(client, manager.Version, e.Callback);
            var binding = new Binding(this, resource, $"{AppIdPrefix}:{client.RawHandle:x}", ++_nextBinding, allowed);
            resource.SetDescription += (_, d) => binding.Description = d.Description ?? string.Empty;
            resource.AddKeyboardTrigger += (_, t) => binding.AddTrigger(t.Keysyms);
            resource.ClearTriggers += (_, _) => binding.ClearTriggers();
            resource.Arm += (_, a) => binding.Arm((uint)a.ArmFlags);
            resource.Disarm += (_, _) => binding.Disarm(except: null);
            resource.Destroyed += (_, _) => binding.Disarm(except: null);
        };
    }

    private sealed class Binding(
        ActionBindingManager owner, GamescopeActionBindingResource resource, string appId, ulong number, bool allowed)
    {
        private readonly List<(uint Keysym, Modifiers Modifiers)> _triggers = [];
        private readonly List<string> _registered = [];
        private bool _armed;

        public GamescopeActionBindingResource Resource => resource;

        public string Description { get; set; } = string.Empty;

        public GlobalShortcutFlags Flags { get; private set; }

        public void AddTrigger(byte[]? array)
        {
            var bytes = array ?? [];
            var keysyms = new uint[bytes.Length / 4];
            for (var i = 0; i < keysyms.Length; i++)
            {
                keysyms[i] = BitConverter.ToUInt32(bytes, i * 4);
            }

            if (!TryChord(keysyms, out var keysym, out var modifiers))
            {
                Log.Warn($"action binding {number}: a trigger with more than one non-modifier key is not supported and never fires");
                return;
            }

            _triggers.Add((keysym, modifiers));
            if (_armed)
            {
                Register();
            }
        }

        public void ClearTriggers()
        {
            _triggers.Clear();
            if (_armed)
            {
                Register();
            }
        }

        public void Arm(uint flags)
        {
            Flags = (GlobalShortcutFlags)(flags & 3);
            _armed = true;
            Register();
        }

        public void Disarm(string? except)
        {
            foreach (var id in _registered)
            {
                if (id != except)
                {
                    owner._byShortcut.Remove((appId, id));
                    owner._registry?.Unregister(appId, id);
                }
            }

            _registered.Clear();
            _armed = false;
        }

        private void Register()
        {
            foreach (var id in _registered)
            {
                owner._byShortcut.Remove((appId, id));
                owner._registry?.Unregister(appId, id);
            }

            _registered.Clear();
            if (!allowed || owner._registry is not { } registry)
            {
                return;
            }

            for (var i = 0; i < _triggers.Count; i++)
            {
                var (keysym, modifiers) = _triggers[i];
                var id = $"{number}/{i}";
                var info = new GlobalShortcutInfo(appId, id, Description, "", TriggerSyntax.Format(keysym, modifiers), Flags);
                if (registry.TryRegister(in info))
                {
                    _registered.Add(id);
                    owner._byShortcut[(appId, id)] = this;
                }
            }
        }
    }
}
