using System.Globalization;
using Basin.Capabilities;

namespace Basin.Ipc;

internal static class IpcTargetSelector
{
    private const string NodePrefix = "node:";
    private const string LayerPrefix = "layer:";
    private const string X11Prefix = "x11:";
    private const string PopupSuffix = "/popup";

    public static bool TryResolve(
        ref IpcParams parameters, IpcDescribe describe, ISceneDiagnostics diagnostics, int margin, IpcReply reply, out IpcTarget target)
    {
        target = default;
        if (!parameters.TryGetRaw("window", out var raw))
        {
            reply.Error(IpcErrorCodes.InvalidParams, "name a 'window': a window selector");
            return false;
        }

        string selector;
        if (raw.Length > 0 && raw[0] == (byte)'"')
        {
            if (!parameters.TryGetString("window", out selector))
            {
                return false;
            }
        }
        else
        {
            var id = parameters.GetUlong("window");
            if (parameters.Failed)
            {
                return false;
            }

            selector = id.ToString(CultureInfo.InvariantCulture);
        }

        return TryResolve(selector, describe, diagnostics, margin, reply, out target);
    }

    public static bool TryResolve(
        string selector, IpcDescribe describe, ISceneDiagnostics diagnostics, int margin, IpcReply reply, out IpcTarget target)
    {
        target = default;
        if (selector.Length == 0)
        {
            reply.Error(IpcErrorCodes.InvalidParams, "'window' is empty");
            return false;
        }

        if (selector.StartsWith(NodePrefix, StringComparison.Ordinal))
        {
            if (diagnostics.LocateNode(selector[NodePrefix.Length..], margin, out var error) is not { } node)
            {
                reply.Error(IpcErrorCodes.NotFound, error ?? $"no node at '{selector}'");
                return false;
            }

            target = new IpcTarget("node", selector, null, null, node);
            return true;
        }

        if (selector.StartsWith(LayerPrefix, StringComparison.Ordinal))
        {
            var space = selector[LayerPrefix.Length..];
            var layers = new List<LayerInfo>();
            describe.Find<ILayerDirectory>()?.CollectLayers(layers);
            foreach (var layer in layers)
            {
                if (layer.Namespace == space && diagnostics.LocateSurface(layer.Surface, margin) is { } follow)
                {
                    target = new IpcTarget("layer", selector, null, layer.Surface, follow);
                    return true;
                }
            }

            reply.Error(IpcErrorCodes.NotFound, $"no mapped layer surface with namespace '{space}'");
            return false;
        }

        if (describe.Toplevels is not { } model)
        {
            reply.Error(IpcErrorCodes.Unavailable, "this compositor has no window model");
            return false;
        }

        var popupAt = selector.IndexOf(PopupSuffix, StringComparison.Ordinal);
        var basePart = popupAt < 0 ? selector : selector[..popupAt];
        var x11 = basePart.StartsWith(X11Prefix, StringComparison.Ordinal);
        var name = x11 ? basePart[X11Prefix.Length..] : basePart;
        Func<ToplevelInfo, bool>? only = x11 ? static info => info.X11Display is not null : null;
        if (!IpcWindowSelector.TryFind(model, name, reply, out var info, only))
        {
            return false;
        }

        var kind = info.X11Display is not null ? "x11" : "window";
        if (popupAt < 0)
        {
            if (info.Surface is not { } surface || diagnostics.LocateWindow(surface, margin) is not { } follow)
            {
                reply.Error(IpcErrorCodes.Refused, $"window {info.Id} is not in the scene");
                return false;
            }

            target = new IpcTarget(kind, selector, info, surface, follow);
            return true;
        }

        var rest = selector[(popupAt + PopupSuffix.Length)..];
        int? index = null;
        if (rest.Length > 0)
        {
            if (rest[0] != ':' || !int.TryParse(rest.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            {
                reply.Error(IpcErrorCodes.InvalidParams, $"'{selector}' ends in neither /popup nor /popup:N");
                return false;
            }

            index = parsed;
        }

        var popups = new List<Surface>();
        if (info.Surface is { } owner)
        {
            describe.Find<IPopupDirectory>()?.CollectPopups(owner, popups);
        }

        if (popups.Count == 0 || (index is { } wanted && wanted >= popups.Count))
        {
            reply.Error(IpcErrorCodes.NotFound, popups.Count == 0
                ? $"window {info.Id} has no mapped popup"
                : $"window {info.Id} has {popups.Count} popups, so no popup:{index}");
            return false;
        }

        var popup = popups[index ?? popups.Count - 1];
        if (diagnostics.LocateSurface(popup, margin) is not { } popupFollow)
        {
            reply.Error(IpcErrorCodes.Refused, $"the popup of window {info.Id} is not in the scene");
            return false;
        }

        target = new IpcTarget("popup", selector, info, popup, popupFollow);
        return true;
    }
}
