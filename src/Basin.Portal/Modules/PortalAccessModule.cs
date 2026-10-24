using Basin.Capabilities;
using Basin.Portal.DBus;
using Tmds.DBus.Protocol;
using static Basin.Portal.PortalLog;

namespace Basin.Portal;

/// <summary>
/// Serves <c>org.freedesktop.impl.portal.Access</c>. It shows the access dialog of the frontend as a confirm prompt through <see cref="IPortalPrompts"/>.
/// </summary>
public sealed class PortalAccessModule : PortalModule, IAccessHandler
{
    private IPortalPrompts _prompts = null!;

    /// <inheritdoc/>
    public override string WireInterface => "org.freedesktop.impl.portal.Access";

    /// <inheritdoc/>
    public override int Version => 1;

    /// <inheritdoc/>
    public override IReadOnlyList<Type> Capabilities => [typeof(IAppInfoResolver)];

    /// <inheritdoc/>
    public override IReadOnlyList<Type> Drivers => [typeof(IPortalPrompts)];

    internal override DBusHandler.DBusInterface Interface => DBusHandler.DBusInterface.OrgFreedesktopImplPortalAccess;

    /// <inheritdoc/>
    protected override void Bind(BasinServices services) => _prompts = services.Require<IPortalPrompts>();

    /// <summary>
    /// Asks the user to grant or deny an access request. A <c>Close</c> on the request cancels the prompt.
    /// </summary>
    /// <param name="handle">The request object path.</param>
    /// <param name="appId">The application that asks.</param>
    /// <param name="parentWindow">The parent window identifier of the application.</param>
    /// <param name="title">The prompt title.</param>
    /// <param name="subtitle">The first line of the prompt text.</param>
    /// <param name="body">More prompt text, which can be empty.</param>
    /// <param name="options">The <c>modal</c> option. Other options are ignored.</param>
    /// <returns><see cref="PortalResponse.Success"/> when the user grants access, otherwise a cancel or other response, with empty results.</returns>
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
