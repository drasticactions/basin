using Basin.Capabilities;
using Basin.Config;
using Basin.Portal.DBus;
using Tmds.DBus.Protocol;
using static Basin.Portal.PortalLog;

namespace Basin.Portal;

public sealed class PortalGlobalShortcutsModule : PortalModule, IGlobalShortcutsHandler, IGlobalShortcutsProperties
{
    private IGlobalShortcuts _registry = null!;
    private IPortalPrompts _prompts = null!;
    private PortalOptions _options = new();
    private GlobalShortcutInfo[] _scratch = new GlobalShortcutInfo[32];

    public override string WireInterface => "org.freedesktop.impl.portal.GlobalShortcuts";

    public override int Version => 2;

    public override IReadOnlyList<Type> Capabilities => [typeof(IAppInfoResolver)];

    public override IReadOnlyList<Type> Drivers => [typeof(IGlobalShortcuts), typeof(IPortalPrompts)];

    internal override DBusHandler.DBusInterface Interface => DBusHandler.DBusInterface.OrgFreedesktopImplPortalGlobalShortcuts;

    uint IGlobalShortcutsProperties.Version => (uint)Version;

    protected override void Bind(BasinServices services)
    {
        _registry = services.Require<IGlobalShortcuts>();
        _prompts = services.Require<IPortalPrompts>();
        _options = services.Find<PortalOptions>() ?? new PortalOptions();
    }

    ValueTask IGlobalShortcutsHandler.HandleGetPropertyAsync(IGlobalShortcutsHandler.GetPropertyContext context) => context.Handle(this);

    ValueTask IGlobalShortcutsHandler.HandleGetAllPropertiesAsync(IGlobalShortcutsHandler.GetAllPropertiesContext context) => context.Handle(this);

    public ValueTask<(uint Response, Dictionary<string, VariantValue> Results)> CreateSessionAsync(
        ObjectPath handle, ObjectPath sessionHandle, string appId, Dictionary<string, VariantValue> options)
    {
        var bus = Bus ?? throw new InvalidOperationException("the portal bus is not connected");
        var session = new GlobalShortcutsSession(this, bus, sessionHandle, appId);
        bus.Register(session);
        _registry.AddObserver(session);
        return ValueTask.FromResult((PortalResponse.Success, Results.Empty));
    }

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
                var info = new GlobalShortcutInfo(session.AppId, id, description, "", preferred);
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

    public ValueTask<(uint Response, Dictionary<string, VariantValue> Results)> ListShortcutsAsync(ObjectPath handle, ObjectPath sessionHandle)
    {
        if (Bus?.SessionAt(sessionHandle.ToString()) is not GlobalShortcutsSession session)
        {
            return ValueTask.FromResult((PortalResponse.Other, Results.Empty));
        }

        return ValueTask.FromResult((PortalResponse.Success, Results.With("shortcuts", Describe(session))));
    }

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
