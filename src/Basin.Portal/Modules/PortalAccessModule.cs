using Basin.Capabilities;
using Basin.Portal.DBus;
using Tmds.DBus.Protocol;
using static Basin.Portal.PortalLog;

namespace Basin.Portal;

public sealed class PortalAccessModule : PortalModule, IAccessHandler
{
    private IPortalPrompts _prompts = null!;

    public override string WireInterface => "org.freedesktop.impl.portal.Access";

    public override int Version => 1;

    public override IReadOnlyList<Type> Capabilities => [typeof(IAppInfoResolver)];

    public override IReadOnlyList<Type> Drivers => [typeof(IPortalPrompts)];

    internal override DBusHandler.DBusInterface Interface => DBusHandler.DBusInterface.OrgFreedesktopImplPortalAccess;

    protected override void Bind(BasinServices services) => _prompts = services.Require<IPortalPrompts>();

    public async ValueTask<(uint Response, Dictionary<string, VariantValue> Results)> AccessDialogAsync(
        ObjectPath handle, string appId, string parentWindow, string title, string subtitle, string body, Dictionary<string, VariantValue> options)
    {
        var request = TrackRequest(handle);
        try
        {
            var modal = Vardict.Bool(options, "modal", true);
            var text = string.IsNullOrEmpty(body) ? subtitle : $"{subtitle}\n{body}";
            var answer = await _prompts.Confirm(Named(new ConfirmPrompt(appId, parentWindow, modal, title, text)), request.Token).ConfigureAwait(true);
            var response = answer.IsAccepted && answer.Value
                ? PortalResponse.Success
                : PortalScreenshotModule.ResponseFor(answer.Response, request);
            Log.Info($"access dialog for {appId} \"{title}\": {(response == 0 ? "granted" : "denied")}");
            return (response, Results.Empty);
        }
        finally
        {
            ReleaseRequest(request);
        }
    }
}
