using Basin.Capabilities;
using Basin.Config;
using Basin.Portal.DBus;
using Tmds.DBus.Protocol;
using static Basin.Portal.PortalLog;

namespace Basin.Portal;

/// <summary>
/// Serves <c>org.freedesktop.impl.portal.GlobalShortcuts</c> over an <see cref="IGlobalShortcuts"/> registry. It asks the user for the triggers of the shortcuts that have none.
/// </summary>
public sealed class PortalGlobalShortcutsModule : PortalModule, IGlobalShortcutsHandler, IGlobalShortcutsProperties
{
    private IGlobalShortcuts _registry = null!;
    private IPortalPrompts _prompts = null!;
    private PortalOptions _options = new();
    private GlobalShortcutInfo[] _scratch = new GlobalShortcutInfo[32];

    /// <inheritdoc/>
    public override string WireInterface => "org.freedesktop.impl.portal.GlobalShortcuts";

    /// <inheritdoc/>
    public override int Version => 2;

    /// <inheritdoc/>
    public override IReadOnlyList<Type> Capabilities => [typeof(IAppInfoResolver)];

    /// <inheritdoc/>
    public override IReadOnlyList<Type> Drivers => [typeof(IGlobalShortcuts), typeof(IPortalPrompts)];

    internal override DBusHandler.DBusInterface Interface => DBusHandler.DBusInterface.OrgFreedesktopImplPortalGlobalShortcuts;

    uint IGlobalShortcutsProperties.Version => (uint)Version;

    /// <inheritdoc/>
    protected override void Bind(BasinServices services)
    {
        _registry = services.Require<IGlobalShortcuts>();
        _prompts = services.Require<IPortalPrompts>();
        _options = services.Find<PortalOptions>() ?? new PortalOptions();
    }

    ValueTask IGlobalShortcutsHandler.HandleGetPropertyAsync(IGlobalShortcutsHandler.GetPropertyContext context) => context.Handle(this);

    ValueTask IGlobalShortcutsHandler.HandleGetAllPropertiesAsync(IGlobalShortcutsHandler.GetAllPropertiesContext context) => context.Handle(this);

    /// <summary>
    /// Creates a shortcuts session and registers it as an observer of the registry.
    /// </summary>
    /// <param name="handle">The request object path.</param>
    /// <param name="sessionHandle">The session object path.</param>
    /// <param name="appId">The application that owns the session.</param>
    /// <param name="options">No options are read.</param>
    /// <returns><see cref="PortalResponse.Success"/> with empty results.</returns>
    /// <exception cref="InvalidOperationException">The module is not installed.</exception>
    public ValueTask<(uint Response, Dictionary<string, VariantValue> Results)> CreateSessionAsync(
        ObjectPath handle, ObjectPath sessionHandle, string appId, Dictionary<string, VariantValue> options)
    {
        var bus = Bus ?? throw new InvalidOperationException("the portal bus is not connected");
        var session = new GlobalShortcutsSession(this, bus, sessionHandle, appId);
        bus.Register(session);
        _registry.AddObserver(session);
        return ValueTask.FromResult((PortalResponse.Success, Results.Empty));
    }

    /// <summary>
    /// Registers the shortcuts for the session's application, then prompts for a trigger for each shortcut that has none.
    /// </summary>
    /// <param name="handle">The request object path.</param>
    /// <param name="sessionHandle">The shortcuts session.</param>
    /// <param name="shortcuts">The id and the properties of each shortcut.</param>
    /// <param name="parentWindow">The parent window identifier of the application.</param>
    /// <param name="options">The <c>modal</c> option.</param>
    /// <returns>The <c>shortcuts</c> result with the bound shortcuts, or <see cref="PortalResponse.Other"/> when the session does not exist or the request closed.</returns>
    public async ValueTask<(uint Response, Dictionary<string, VariantValue> Results)> BindShortcutsAsync(
        ObjectPath handle, ObjectPath sessionHandle, (string, Dictionary<string, VariantValue>)[] shortcuts, string parentWindow,
        Dictionary<string, VariantValue> options)
    {
        if (Bus?.SessionAt(sessionHandle.ToString()) is not GlobalShortcutsSession session)
        {
            return (PortalResponse.Other, Results.Empty);
        }

        var request = TrackRequest(handle);
        try
        {
            var rows = new List<ShortcutPromptRow>();
            foreach (var (id, properties) in shortcuts)
            {
                var description = Vardict.String(properties, "description");
                var preferred = _options.AcceptPreferredTriggers ? Vardict.String(properties, "preferred_trigger") : "";
                var flags = (Vardict.Bool(properties, "basin.no-block") ? GlobalShortcutFlags.NoBlock : GlobalShortcutFlags.None)
                    | (Vardict.Bool(properties, "basin.one-shot") ? GlobalShortcutFlags.OneShot : GlobalShortcutFlags.None);
                var info = new GlobalShortcutInfo(session.AppId, id, description, "", preferred, flags);
                if (!_registry.TryRegister(in info))
                {
                    _registry.Unregister(session.AppId, id);
                    _ = _registry.TryRegister(in info);
                }

                if (!session.BoundList.Contains(id))
                {
                    session.BoundList.Add(id);
                }

                var bound = Lookup(session.AppId, id);
                rows.Add(new ShortcutPromptRow(id, description, preferred, !string.IsNullOrEmpty(preferred) && string.IsNullOrEmpty(bound.TriggerDescription), bound.TriggerDescription));
            }

            session.BindCalled = true;
            var unbound = rows.Where(row => string.IsNullOrEmpty(row.CurrentTrigger)).ToList();
            if (unbound.Count > 0)
            {
                var answer = await _prompts.BindShortcuts(Named(new ShortcutPrompt(session.AppId, parentWindow, Vardict.Bool(options, "modal", true), unbound)), request.Token).ConfigureAwait(true);
                if (answer.Response == PromptResponse.Cancelled && request.IsClosed)
                {
                    return (PortalResponse.Other, Results.Empty);
                }

                if (answer.IsAccepted && answer.Value is { } bindings)
                {
                    Apply(session, bindings, rows);
                }
            }

            return (PortalResponse.Success, Results.With("shortcuts", Describe(session)));
        }
        finally
        {
            ReleaseRequest(request);
        }
    }

    /// <summary>
    /// Lists the shortcuts that the session bound, with their current triggers.
    /// </summary>
    /// <param name="handle">The request object path.</param>
    /// <param name="sessionHandle">The shortcuts session.</param>
    /// <returns>The <c>shortcuts</c> result, or <see cref="PortalResponse.Other"/> when the session does not exist.</returns>
    public ValueTask<(uint Response, Dictionary<string, VariantValue> Results)> ListShortcutsAsync(ObjectPath handle, ObjectPath sessionHandle)
    {
        if (Bus?.SessionAt(sessionHandle.ToString()) is not GlobalShortcutsSession session)
        {
            return ValueTask.FromResult((PortalResponse.Other, Results.Empty));
        }

        return ValueTask.FromResult((PortalResponse.Success, Results.With("shortcuts", Describe(session))));
    }

    /// <summary>
    /// Prompts the user to change the triggers of the application's shortcuts, then sends <c>ShortcutsChanged</c>.
    /// </summary>
    /// <param name="sessionHandle">The shortcuts session.</param>
    /// <param name="parentWindow">The parent window identifier of the application.</param>
    /// <param name="options">The <c>modal</c> option.</param>
    /// <returns>A task that completes when the prompt closes.</returns>
    /// <exception cref="DBusErrorReplyException">The session does not exist.</exception>
    public async ValueTask ConfigureShortcutsAsync(ObjectPath sessionHandle, string parentWindow, Dictionary<string, VariantValue> options)
    {
        if (Bus?.SessionAt(sessionHandle.ToString()) is not GlobalShortcutsSession session)
        {
            throw PortalError.NoObject(sessionHandle.ToString());
        }

        var rows = new List<ShortcutPromptRow>();
        foreach (var info in Registered(session.AppId))
        {
            rows.Add(new ShortcutPromptRow(info.Id, info.Description, info.PreferredTrigger, false, info.TriggerDescription));
        }

        var answer = await _prompts.BindShortcuts(Named(new ShortcutPrompt(session.AppId, parentWindow, Vardict.Bool(options, "modal", true), rows)), CancellationToken.None).ConfigureAwait(true);
        if (answer.IsAccepted && answer.Value is { } bindings)
        {
            Apply(session, bindings, rows);
        }

        try
        {
            Bus.Connection?.EmitShortcutsChanged(new ObjectPath(PortalBus.RootPath), session.Handle, DescribeRows(session));
        }
        catch (Exception e) when (e is DBusExceptionBase or ObjectDisposedException or InvalidOperationException)
        {
            Log.Debug($"shortcut session {session.Id}: ShortcutsChanged not sent: {e.Message}");
        }
    }

    internal void Release(GlobalShortcutsSession session)
    {
        _registry.RemoveObserver(session);
        foreach (var id in session.BoundList)
        {
            _registry.Unregister(session.AppId, id);
        }

        session.BoundList.Clear();
    }

    private void Apply(GlobalShortcutsSession session, ShortcutBinding[] bindings, List<ShortcutPromptRow> rows)
    {
        foreach (var binding in bindings)
        {
            var row = rows.FirstOrDefault(r => r.Id == binding.Id);
            if (row.Id is null)
            {
                continue;
            }

            var info = new GlobalShortcutInfo(session.AppId, binding.Id, row.Description, "", binding.Trigger);
            _registry.Unregister(session.AppId, binding.Id);
            if (_registry.TryRegister(in info) && !session.BoundList.Contains(binding.Id))
            {
                session.BoundList.Add(binding.Id);
            }
        }
    }

    private GlobalShortcutInfo Lookup(string appId, string id)
    {
        foreach (var info in Registered(appId))
        {
            if (info.Id == id)
            {
                return info;
            }
        }

        return default;
    }

    private IEnumerable<GlobalShortcutInfo> Registered(string appId)
    {
        int count;
        while ((count = _registry.Enumerate(_scratch)) < 0)
        {
            _scratch = new GlobalShortcutInfo[_scratch.Length * 2];
        }

        var list = new List<GlobalShortcutInfo>();
        for (var i = 0; i < count; i++)
        {
            if (_scratch[i].AppId == appId)
            {
                list.Add(_scratch[i]);
            }
        }

        return list;
    }

    private VariantValue Describe(GlobalShortcutsSession session) => Results.Shortcuts(DescribeRows(session));

    private (string, Dictionary<string, VariantValue>)[] DescribeRows(GlobalShortcutsSession session)
    {
        var rows = new List<(string, Dictionary<string, VariantValue>)>();
        foreach (var info in Registered(session.AppId))
        {
            if (!session.BoundList.Contains(info.Id))
            {
                continue;
            }

            rows.Add((info.Id, new Dictionary<string, VariantValue>
            {
                ["description"] = VariantValue.String(info.Description),
                ["trigger_description"] = VariantValue.String(info.TriggerDescription),
            }));
        }

        return rows.ToArray();
    }
}
