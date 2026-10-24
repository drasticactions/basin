using System.Globalization;
using Basin;
using Basin.Capabilities;

using Basin.Diagnostics;

namespace Westonia;

internal sealed partial class Westonia
{
    private string? SystemLayout { get; set; }

    private void ApplyKeyboardConfig()
    {
        var names = Basin.Seat.SystemKeymap.Read();
        SystemLayout = names.Layout;
        Seat.Keyboard.SetKeymap(names);
    }

    private void AttachInput()
    {
        _inputConfigurator.Rejected += outcome =>
        {
            if (outcome.Device != 0 || _input is not null)
            {
                if (outcome.Result == InputSettingResult.Unsupported)
                {
                    _log.Debug($"weston.ini: {InputSettingNames.Key(outcome.Setting)} on {outcome.DeviceName ?? "every device"}: {outcome.Result}");
                }
                else
                {
                    _log.Info($"weston.ini: {InputSettingNames.Key(outcome.Setting)} on {outcome.DeviceName ?? "every device"}: {outcome.Result}");
                }
            }
            else
            {
                _log.Warn($"weston.ini: {InputSettingNames.Key(outcome.Setting)} is set but this session has no input device configuration");
            }
        };
        _inputConfigurator.Apply(WestonInputRules.From(_ini, message => _log.Warn($"{message}")));
        _inputConfigurator.Attach(Seat, _seat!.Binder, _input?.Configuration);
        _seat.Binder.VtSwitching = _ini.Keyboard.VtSwitching;
        var keyboard = _ini.Keyboard;
        _report.Line($"KEYMAP {(_inputConfigurator.NamesSeatKeymap ? "weston.ini" : "system")} layout={keyboard.Layout ?? SystemLayout ?? "default"} " + $"compiled={(Seat.Keyboard.Keymap is null ? "no" : "yes")} " + $"repeat={keyboard.RepeatRate}/{keyboard.RepeatDelay}");
    }

    private void ApplyOutputConfig()
    {
        foreach (var view in _outputs.Views)
        {
            var section = _ini.Outputs.FirstOrDefault(o =>
                string.Equals(o.Name, view.Output.Name, StringComparison.OrdinalIgnoreCase));
            if (section is null)
            {
                continue;
            }

            using var state = new OutputState();
            var touched = false;

            if (section.Scale > 0 && Math.Abs(section.Scale - view.Output.Scale) > double.Epsilon)
            {
                state.SetScale(section.Scale);
                touched = true;
            }

            if (section.Transform is { Length: > 0 } transform && TransformOf(transform) is { } value)
            {
                state.SetTransform(value);
                touched = true;
            }

            if (section.Mode is { Length: > 0 } mode && ModeOf(view.Output, mode) is { } chosen)
            {
                state.SetMode(chosen);
                touched = true;
            }

            if (touched && !view.Output.Commit(state))
            {
                _log.Warn($"weston.ini: [output] {section.Name} refused its configuration");
            }

            if (section.IccProfile is { Length: > 0 } profile)
            {
                _log.Info($"weston.ini: [output] {section.Name} icc_profile={profile} applied through the color manager");
            }
        }
    }

    private static OutputTransform? TransformOf(string name) => name switch
    {
        "normal" => OutputTransform.Normal,
        "90" => OutputTransform.Rotate90,
        "180" => OutputTransform.Rotate180,
        "270" => OutputTransform.Rotate270,
        "flipped" => OutputTransform.Flipped,
        "flipped-90" => OutputTransform.Flipped90,
        "flipped-180" => OutputTransform.Flipped180,
        "flipped-270" => OutputTransform.Flipped270,
        _ => null,
    };

    private static OutputMode? ModeOf(IOutput output, string mode)
    {
        var parts = mode.Split(['x', '@'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 ||
            !int.TryParse(parts[0], out var width) ||
            !int.TryParse(parts[1], out var height))
        {
            return null;
        }

        if (output is not Basin.Backend.Drm.DrmOutput card)
        {
            return new OutputMode(width, height, output.CurrentMode.RefreshMilliHz);
        }

        OutputMode? best = null;
        foreach (var candidate in card.Modes)
        {
            if (candidate.Width == width && candidate.Height == height &&
                (best is null || candidate.RefreshMilliHz > best.Value.RefreshMilliHz))
            {
                best = candidate;
            }
        }

        return best;
    }

    private void SpawnInputMethod()
    {
        if (_ini.InputMethodPath is not { Length: > 0 } path)
        {
            return;
        }

        var command = _ini.InputMethodArgs is { Length: > 0 } args ? $"{path} {args}" : path;
        Spawn(command);
        _log.Info($"input: started the input method {command}");
    }
}
