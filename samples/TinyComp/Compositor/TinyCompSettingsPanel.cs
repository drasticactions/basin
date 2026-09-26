using Basin;
using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Host;
using Basin.Ipc;
using Basin.Scene;
using Basin.UI.Paper;

namespace TinyComp;

internal sealed partial class TinyComp
{
    private const int SettingsWidth = 880;
    private const int SettingsHeight = 620;
    private const string SettingsLog = "settings";

    private PaperUIHost? _paperHost;
    private UIDriver? _paperDriver;
    private PaperClipboard? _paperClipboard;
    private UISurfaceNode? _settingsNode;
    private PaperSurface? _settingsSurface;
    private SettingsDraft? _settingsDraft;
    private SettingsForm? _settingsForm;
    private string? _settingsUnavailable;
    private bool _settingsApplyQueued;
    private bool _settingsQuiet;
    private (double CursorX, double CursorY, int NodeX, int NodeY)? _settingsDrag;
    private HashSet<string> _settingsBaseline = new(StringComparer.Ordinal);
    private string? _settingsBaselineFatal;
    private readonly Dictionary<string, IReadOnlyList<string>> _shaderParameterNames = new(StringComparer.Ordinal);

    internal bool SettingsOpen => _settingsNode is not null;

    private string? SettingsPath => _configPath == "false" ? null : _configPath ?? Config.DefaultPath();

    private SettingsDraft SettingsDraftOrOpen()
    {
        if (_settingsDraft is { } draft)
        {
            return draft;
        }

        draft = SettingsDraft.Open(SettingsPath);
        draft.Changed += OnSettingsDraftChanged;
        _settingsDraft = draft;
        RememberSettingsBaseline();
        return draft;
    }

    private void RememberSettingsBaseline()
    {
        if (_settingsDraft is not { } draft)
        {
            return;
        }

        _ = ParseSettings(draft.FileText, out var warnings, out _settingsBaselineFatal);
        _settingsBaseline = new HashSet<string>(warnings, StringComparer.Ordinal);
    }

    private void ReloadSettingsDraft()
    {
        if (_settingsDraft is not { } draft)
        {
            return;
        }

        _settingsQuiet = true;
        try
        {
            draft.Revert("reloaded from file");
        }
        finally
        {
            _settingsQuiet = false;
        }

        RememberSettingsBaseline();
    }

    private PaperUIHost? PaperHost()
    {
        if (_paperHost is not null)
        {
            return _paperHost;
        }

        if (_settingsUnavailable is not null)
        {
            return null;
        }

        if (QuillHost() is not { } quill)
        {
            _settingsUnavailable = _quillDeclined ?? "no Quill host could be built";
            _log.Warn($"settings: unavailable on {_rendererName}: {_settingsUnavailable}");
            return null;
        }

        _paperHost = new PaperUIHost(quill);
        _paperDriver = new UIDriver(_paperHost, _loop);
        _paperDriver.Start();
        return _paperHost;
    }

    internal string? ToggleSettings() => SettingsOpen ? CloseSettings() : OpenSettings();

    internal string? OpenSettings()
    {
        if (_settingsSurface is { } open)
        {
            FocusUISurface(open);
            return null;
        }

        if (PaperHost() is not { } host)
        {
            return _settingsUnavailable;
        }

        if ((ViewAtCursor() ?? Views.FirstOrDefault()) is not { } view)
        {
            return "no output";
        }

        var draft = SettingsDraftOrOpen();
        var font = Basin.Frames.Quill.QuillFrameFonts.Bundled();
        _settingsForm = new SettingsForm(draft, SettingsContextFor(font)) { Section = _settingsForm?.Section ?? 0 };

        var box = _layout.BoxOf(view.Output);
        var width = Math.Min(SettingsWidth, Math.Max(200, box.Width - 16));
        var height = Math.Min(SettingsHeight, Math.Max(160, box.Height - 16));
        var node = new UISurfaceNode(_layers.Overlay, host, UIIndex) { Target = UITargetKind.Dmabuf, InputEnabled = true };
        node.Faulted += error => _log.Error($"settings: {error.Message}");
        node.SetPosition(box.X + ((box.Width - width) / 2), box.Y + ((box.Height - height) / 2));
        if (!node.Configure(width, height, view.Output.Scale) || node.Surface is not PaperSurface surface)
        {
            node.Dispose();
            return "the settings surface could not be built";
        }

        var fallbacks = PaperFonts.AddFallbacks(surface.Paper, font);
        _log.Debug($"settings: {fallbacks} fallback faces");
        surface.Build = _settingsForm.Build;
        surface.KeyText = _seat.Keyboard;
        surface.CursorChanged += OnSettingsCursor;
        surface.Faulted += error => _log.Error($"settings: the panel threw: {error}");
        if (_services.Find<ISelectionStore>() is { } store)
        {
            _paperClipboard ??= new PaperClipboard(store, _loop);
            surface.Paper.SetClipboardHandler(_paperClipboard);
        }

        _settingsNode = node;
        _settingsSurface = surface;
        _chordCapture.Changed += OnChordCaptureChanged;
        FocusUISurface(surface);
        RefreshPointer();
        return null;
    }

    internal string? CloseSettings()
    {
        if (_settingsNode is not { } node)
        {
            return null;
        }

        _settingsDrag = null;
        _chordCapture.Changed -= OnChordCaptureChanged;
        _chordCapture.Cancel();
        if (ReferenceEquals(_uiRouter?.KeyboardFocus, _settingsSurface))
        {
            ReleaseUIKeyboard(restore: true);
        }

        _settingsNode = null;
        _settingsSurface = null;
        node.Dispose();
        RefreshPointer();
        return null;
    }

    private SettingsContext SettingsContextFor(Prowl.Scribe.FontFile font) => new()
    {
        Renderers = Basin.Renderers.RendererCatalog.Names,
        MetacityThemes = Basin.Frames.Metacity.MetacityThemes.Available(),
        FromFlags = _config.FromFlags,
        Outputs = SettingsOutputs(),
        Capture = _chordCapture,
        Font = font,
        ShaderParameters = ShaderParameterNames,
        Close = () => CloseSettings(),
        Save = () => SaveSettings(overwrite: false),
        Overwrite = () => SaveSettings(overwrite: true),
        Revert = RevertSettings,
        DragStart = BeginSettingsDrag,
    };

    private List<SettingsOutput> SettingsOutputs()
    {
        var outputs = new List<SettingsOutput>();
        foreach (var view in Views)
        {
            var modes = new List<string>();
            if (view.Output is Basin.Backend.Drm.DrmOutput drm)
            {
                foreach (var mode in drm.Modes)
                {
                    var text = $"{mode.Width}x{mode.Height}@{(int)Math.Round(mode.RefreshMilliHz / 1000.0)}";
                    if (!modes.Contains(text))
                    {
                        modes.Add(text);
                    }
                }
            }
            else
            {
                var mode = view.Output.CurrentMode;
                modes.Add($"{mode.Width}x{mode.Height}");
            }

            outputs.Add(new SettingsOutput(view.Output.Name, modes));
        }

        return outputs;
    }

    private IReadOnlyList<string> ShaderParameterNames(string path)
    {
        if (_shaderParameterNames.TryGetValue(path, out var known))
        {
            return known;
        }

        var names = new List<string>();
        if (File.Exists(path) && Basin.Rashader.RashaderLibrary.IsAvailable(out _))
        {
            using var preset = Basin.Rashader.RashaderPreset.TryCreate(path, default, out _);
            if (preset is not null)
            {
                foreach (var parameter in preset.Parameters)
                {
                    names.Add(parameter.Name);
                }
            }
        }

        _shaderParameterNames[path] = names;
        return names;
    }

    private void OnSettingsDraftChanged()
    {
        _settingsSurface?.Invalidate();
        if (_settingsQuiet || _settingsApplyQueued)
        {
            return;
        }

        _settingsApplyQueued = true;
        _loop.AddIdle(ApplySettingsDraft);
    }

    private void OnChordCaptureChanged() => _settingsSurface?.Invalidate();

    private void OnSettingsCursor(Prowl.PaperUI.PaperCursor cursor)
    {
        if (_settingsSurface is { } surface && ReferenceEquals(_uiRouter?.Hovered, surface) && _settingsDrag is null)
        {
            _cursor.ShowNamed(PaperCursors.NameOf(cursor));
        }
    }

    private void ApplySettingsDraft()
    {
        _settingsApplyQueued = false;
        if (_settingsDraft is not { } draft)
        {
            return;
        }

        var parsed = ParseSettings(draft.Text, out var warnings, out var fatal);
        string? blocking = null;
        string? advice = null;
        if (fatal is not null && fatal != _settingsBaselineFatal)
        {
            _log.Info($"settings: {fatal}");
            blocking = SettingsMessages.Explain(fatal).Text;
        }

        foreach (var warning in warnings)
        {
            if (_settingsBaseline.Contains(warning))
            {
                continue;
            }

            _log.Info($"settings: {warning}");
            var (text, refused) = SettingsMessages.Explain(warning);
            if (refused)
            {
                blocking ??= text;
            }
            else
            {
                advice ??= text;
            }
        }

        var path = draft.LastPath ?? string.Empty;
        if (blocking is not null)
        {
            draft.SetError(path, blocking);
            _settingsSurface?.Invalidate();
            return;
        }

        draft.SetHint(path, advice);
        draft.ClearErrors();
        _ = CarryOver(parsed);
        _ = ApplyConfig(parsed, crossfade: false);
        _settingsSurface?.Invalidate();
    }

    private Config ParseSettings(string text, out List<string> warnings, out string? fatal)
    {
        var previousSink = BasinLog.Sink;
        var previousLevel = BasinLog.Level;
        var capture = new SettingsLogCapture(previousSink, SettingsLog);
        BasinLog.Sink = capture;
        if (BasinLog.Level > BasinLogLevel.Warn)
        {
            BasinLog.Level = BasinLogLevel.Warn;
        }

        Config parsed;
        try
        {
            parsed = Config.Parse(text, BasinLog.For(SettingsLog), out fatal);
        }
        finally
        {
            BasinLog.Sink = previousSink;
            BasinLog.Level = previousLevel;
        }

        warnings = capture.Warnings;
        return parsed;
    }

    private void SaveSettings(bool overwrite)
    {
        if (_settingsDraft is not { } draft)
        {
            return;
        }

        if (SaveQuietly(draft, overwrite) is { } failure)
        {
            _log.Warn($"settings: {failure}");
        }

        _settingsSurface?.Invalidate();
    }

    private string? SaveQuietly(SettingsDraft draft, bool overwrite)
    {
        _settingsQuiet = true;
        string? failure;
        try
        {
            failure = draft.Save(overwrite);
        }
        finally
        {
            _settingsQuiet = false;
        }

        if (failure is null)
        {
            RememberSettingsBaseline();
        }

        return failure;
    }

    private void RevertSettings()
    {
        if (_settingsDraft is not { } draft)
        {
            return;
        }

        draft.Revert("reverted to the file");
        RememberSettingsBaseline();
    }

    private void BeginSettingsDrag()
    {
        if (_settingsNode is { } node)
        {
            _settingsDrag = (_cursorX, _cursorY, node.X, node.Y);
            _cursor.ShowNamed("grabbing");
        }
    }

    private bool DragSettings(double x, double y)
    {
        if (_settingsDrag is not { } drag || _settingsNode is not { } node)
        {
            return false;
        }

        var bounds = _layout.Bounds;
        var nextX = (int)Math.Round(drag.NodeX + (x - drag.CursorX));
        var nextY = (int)Math.Round(drag.NodeY + (y - drag.CursorY));
        nextX = Math.Clamp(nextX, bounds.X - node.Width + 64, bounds.X + bounds.Width - 64);
        nextY = Math.Clamp(nextY, bounds.Y, bounds.Y + bounds.Height - 32);
        node.SetPosition(nextX, nextY);
        return true;
    }

    private void EndSettingsDrag() => _settingsDrag = null;

    private void DisposeSettings()
    {
        CloseSettings();
        _paperDriver?.Dispose();
        _paperDriver = null;
        _paperHost?.Dispose();
        _paperHost = null;
        _paperClipboard?.Dispose();
        _paperClipboard = null;
    }

    private void RegisterSettingsCommands(IpcMethodRegistry methods)
    {
        _report.Register(methods, "tinycomp/settings", "settings {state:open|close|toggle}", (ref IpcParams p, IpcReply reply) =>
        {
            var state = p.TryGetString("state", out var named) ? named : "toggle";
            if (p.Failed)
            {
                return;
            }

            var failure = state switch
            {
                "open" => OpenSettings(),
                "close" => CloseSettings(),
                "toggle" => ToggleSettings(),
                _ => "state is open, close or toggle",
            };
            if (failure is not null)
            {
                if (state is "open" or "close" or "toggle")
                {
                    reply.Error(IpcErrorCodes.Unavailable, $"unavailable on {_rendererName}: {failure}");
                }
                else
                {
                    reply.Error(IpcErrorCodes.InvalidParams, failure);
                }

                return;
            }

            WriteSettingsState();
        });

        _report.Register(methods, "tinycomp/settings-set", "setting {key} {value}", (ref IpcParams p, IpcReply reply) =>
        {
            var key = p.GetString("key");
            var value = p.GetString("value");
            if (p.Failed)
            {
                return;
            }

            var dot = key.LastIndexOf('.');
            if (dot <= 0 || dot == key.Length - 1)
            {
                reply.Error(IpcErrorCodes.InvalidParams, "key is TABLE.KEY, such as effects.wobbly");
                return;
            }

            if (!Basin.Config.TomlValue.TryParse(value, out var literal))
            {
                reply.Error(IpcErrorCodes.InvalidParams, $"'{value}' is not a TOML value; quote a string");
                return;
            }

            var draft = SettingsDraftOrOpen();
            var table = key[..dot];
            var name = key[(dot + 1)..];
            if (SettingsCatalog.Find(key) is { } catalog)
            {
                draft.Set(catalog, literal);
            }
            else
            {
                draft.Set(table, name, literal);
            }

            ApplySettingsDraft();
            _report.Line($"SETTING {key}={value} applied={(draft.HasErrors ? "no" : "yes")}"
                + (draft.ErrorOf(draft.LastPath ?? string.Empty) is { } why ? $" error={why}" : string.Empty));
            WriteSettingsState();
        });

        _report.Register(methods, "tinycomp/settings-save", "settings-save [{overwrite:bool}]", (ref IpcParams p, IpcReply reply) =>
        {
            var overwrite = p.TryGetBool("overwrite", out var flag) && flag;
            if (p.Failed)
            {
                return;
            }

            var draft = SettingsDraftOrOpen();
            if (SaveQuietly(draft, overwrite) is { } failure)
            {
                reply.Error(draft.Conflict ? IpcErrorCodes.Refused : IpcErrorCodes.Failed, failure);
                return;
            }

            _settingsSurface?.Invalidate();
            WriteSettingsState();
        });

        _report.Register(methods, "tinycomp/settings-revert", "settings-revert", (ref IpcParams p, IpcReply reply) =>
        {
            SettingsDraftOrOpen();
            RevertSettings();
            ApplySettingsDraft();
            WriteSettingsState();
        });

        _report.Register(methods, "tinycomp/settings-state", "settings-state", WriteSettingsState);
    }

    private void WriteSettingsState()
    {
        var draft = _settingsDraft;
        var output = _settingsNode is { } node ? _layout.OutputAt(node.X + (node.Width / 2.0), node.Y + (node.Height / 2.0))?.Name : null;
        _report.Line(
            $"SETTINGS open={(SettingsOpen ? "yes" : "no")} output={output ?? "none"}"
            + (_settingsNode is { } placed ? $" at={placed.X},{placed.Y} size={placed.Width}x{placed.Height} panel-frames={_settingsSurface?.Frames ?? 0}" : string.Empty)
            + $" dirty={draft?.DirtyCount ?? 0}"
            + $" file={SettingsPath ?? "none"} available={(_settingsUnavailable is null ? "yes" : "no")}"
            + $" wobbly={(_config.Wobbly ? "on" : "off")} offload={(_offload ? "on" : "off")} frame-style={_config.FrameStyle.ToString().ToLowerInvariant()}"
            + $" status={(draft?.Status is { Length: > 0 } status ? status.Replace(' ', '-') : "none")}"
            + ConfigSummary(_config));
    }
}
