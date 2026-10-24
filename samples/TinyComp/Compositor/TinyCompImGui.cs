using Basin;
using Basin.Capabilities;
using Basin.Host;
using Basin.UI.ImGui;

namespace TinyComp;

internal sealed partial class TinyComp
{
    private ImGuiUIHost? _imguiHost;
    private UIDriver? _imguiDriver;
    private string? _imguiDeclined;
    private ImGuiDemo? _imguiDemo;
    private bool _uiModifiersWired;

    private ImGuiUIHost? ImGuiHost()
    {
        if (_imguiHost is not null)
        {
            return _imguiHost;
        }

        if (_imguiDeclined is not null)
        {
            return null;
        }

        var options = new ImGuiHostOptions { FontSize = 15f };
        var host = Basin.Renderers.ImGuiHosts.For(
            _renderer, _allocator, _renderNode, out _imguiDeclined, out var vulkanDeclined, options);
        if (vulkanDeclined is not null)
        {
            _log.Warn($"imgui: cannot use the {_rendererName} renderer's VulkanDevice, trying a GlDevice: {vulkanDeclined}");
        }

        if (host is null)
        {
            _imguiDeclined ??= "no ImGui host could be built";
            return null;
        }

        _log.Info($"imgui: draws on the {host.Name} host beside the {_rendererName} renderer");
        _imguiHost = host;
        _imguiDriver = new UIDriver(host, _loop);
        _imguiDriver.Start();
        WireUIModifiers();
        return host;
    }

    private void StartImGuiDemo()
    {
        if (ImGuiHost() is not { } host)
        {
            _log.Warn($"imgui demo: unavailable, {_imguiDeclined}");
            return;
        }

        _imguiDemo = new ImGuiDemo(_layers.Overlay, host, UIIndex, _log);
        _imguiDemo.SurfaceCreated += PrepareImGuiSurface;
        PlaceImGuiDemo();
    }

    private void PlaceImGuiDemo()
    {
        if (_imguiDemo is not { } demo || Views.FirstOrDefault() is not { } view)
        {
            return;
        }

        if (demo.Place(_layout.BoxOf(view.Output), view.Output.Scale))
        {
            RefreshPointer();
        }
    }

    private void PrepareImGuiSurface(ImGuiUISurface surface)
    {
        surface.KeyText = _seat.Keyboard;
        surface.Modifiers = ImGuiModifiersOf(_seat.Keyboard.Keymap);
        if (_services.Find<ISelectionStore>() is { } store)
        {
            surface.SetClipboard(store, _loop);
        }
    }

    private static ImGuiModifierMasks ImGuiModifiersOf(Xkb.XkbKeymap? keymap) =>
        keymap is null
            ? ImGuiModifierMasks.Standard
            : ImGuiModifierMasks.FromIndices(
                keymap.GetModIndex("Control"), keymap.GetModIndex("Shift"),
                keymap.GetModIndex("Mod1"), keymap.GetModIndex("Mod4"));

    private void WireUIModifiers()
    {
        if (_uiModifiersWired)
        {
            return;
        }

        _uiModifiersWired = true;
        _seat.Keyboard.ModifiersChanged += ForwardUIModifiers;
        _seat.Keyboard.KeymapChanged += OnUIKeymapChanged;
        UIRouter.FocusChanged += _ => ForwardUIModifiers();
    }

    private void ForwardUIModifiers()
    {
        var (depressed, latched, locked, group) = _seat.Keyboard.ModifierState;
        _uiRouter?.Modifiers(depressed, latched, locked, group);
    }

    private void OnUIKeymapChanged()
    {
        var masks = ImGuiModifiersOf(_seat.Keyboard.Keymap);
        if (_imguiDemo?.Surface is { } surface)
        {
            surface.Modifiers = masks;
        }

        if (_inspector?.Surface is { } inspector)
        {
            inspector.Modifiers = masks;
        }
    }

    private void DisposeImGui()
    {
        if (_uiModifiersWired)
        {
            _seat.Keyboard.ModifiersChanged -= ForwardUIModifiers;
            _seat.Keyboard.KeymapChanged -= OnUIKeymapChanged;
            _uiModifiersWired = false;
        }

        _imguiDemo?.Dispose();
        _imguiDemo = null;
        DisposeInspector();
        _imguiDriver?.Dispose();
        _imguiDriver = null;
        _imguiHost?.Dispose();
        _imguiHost = null;
    }
}
