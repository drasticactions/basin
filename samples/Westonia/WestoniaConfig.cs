using System.Globalization;
using Basin;
using Basin.Capabilities;
using Microsoft.Extensions.Logging;

namespace Westonia;

internal sealed partial class Westonia
{
    private void ApplyKeyboardConfig()
    {
        var keyboard = _ini.Keyboard;
        var named = keyboard.Layout is not null || keyboard.Model is not null || keyboard.Rules is not null ||
                    keyboard.Variant is not null || keyboard.Options is not null;

        var names = named
            ? new KeymapNames(
                keyboard.Rules, keyboard.Model, keyboard.Layout, keyboard.Variant, keyboard.Options)
            : Basin.Seat.SystemKeymap.Read();

        Seat.Keyboard.SetKeymap(names);
        Seat.Keyboard.SetRepeatInfo(keyboard.RepeatRate, keyboard.RepeatDelay);
        Console.WriteLine(
            $"KEYMAP {(named ? "weston.ini" : "system")} layout={names.Layout ?? "default"} " +
            $"compiled={(Seat.Keyboard.Keymap is null ? "no" : "yes")} " +
            $"repeat={keyboard.RepeatRate}/{keyboard.RepeatDelay}");
    }

    private void ApplyLibinputConfig()
    {
        if (_ini.Libinput.Count == 0)
        {
            return;
        }

        if (_services.Find<IInputDeviceConfiguration>() is not { } configuration)
        {
            _log.LogWarning("[libinput] is set but this session has no input device configuration");
            return;
        }

        Span<InputDeviceInfo> devices = new InputDeviceInfo[32];
        var count = configuration.Enumerate(devices);
        for (var i = 0; i < count; i++)
        {
            foreach (var (key, value) in _ini.Libinput)
            {
                if (SettingFor(key) is not { } setting)
                {
                    _log.LogWarning("weston.ini: [libinput] {Key} is not honoured", key);
                    continue;
                }

                var result = configuration.Set(devices[i].Id, setting, ValueFor(setting, value));
                if (result != InputSettingResult.Success)
                {
                    _log.LogInformation(
                        "[libinput] {Key} on {Device}: {Result}", key, devices[i].Name, result);
                }
            }
        }
    }

    private static InputSetting? SettingFor(string key) => key switch
    {
        "enable-tap" or "tap" => InputSetting.Tap,
        "tap-button-map" => InputSetting.TapButtonMap,
        "tap-and-drag" or "drag" => InputSetting.Drag,
        "tap-and-drag-lock" or "drag-lock" => InputSetting.DragLock,
        "accel-profile" => InputSetting.AccelProfile,
        "accel-speed" => InputSetting.AccelSpeed,
        "natural-scroll" => InputSetting.NaturalScroll,
        "left-handed" => InputSetting.LeftHanded,
        "click-method" => InputSetting.ClickMethod,
        "middle-emulation" => InputSetting.MiddleEmulation,
        "scroll-method" => InputSetting.ScrollMethod,
        "scroll-button" => InputSetting.ScrollButton,
        "disable-while-typing" or "dwt" => InputSetting.DisableWhileTyping,
        "rotation" => InputSetting.Rotation,
        "calibration-matrix" or "calibration_matrix" => InputSetting.CalibrationMatrix,
        "send-events" => InputSetting.SendEvents,
        _ => null,
    };

    private static InputSettingValue ValueFor(InputSetting setting, string value) => setting switch
    {
        InputSetting.AccelSpeed or InputSetting.Rotation =>
            new InputSettingValue(0, [Number(value)]),
        InputSetting.CalibrationMatrix =>
            new InputSettingValue(0, value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)
                .Select(Number).ToArray()),
        InputSetting.TapButtonMap => new InputSettingValue(value == "lrm" ? 0u : 1u),
        InputSetting.ClickMethod => new InputSettingValue(value switch
        {
            "clickfinger" => 2u,
            "button-areas" => 1u,
            _ => 0u,
        }),
        InputSetting.ScrollMethod => new InputSettingValue(value switch
        {
            "two-finger" => 2u,
            "edge" => 4u,
            "button" => 8u,
            _ => 1u,
        }),
        InputSetting.ScrollButton => new InputSettingValue(uint.TryParse(value, out var button) ? button : 0u),
        _ => new InputSettingValue(Truthy(value) ? 1u : 0u),
    };

    private static double Number(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0.0;

    private static bool Truthy(string value) =>
        value.ToLowerInvariant() is "true" or "1" or "yes" or "on" or "enabled";

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
                _log.LogWarning("weston.ini: [output] {Name} refused its configuration", section.Name);
            }

            if (section.IccProfile is { Length: > 0 } profile)
            {
                _log.LogInformation(
                    "weston.ini: [output] {Name} icc_profile={Profile} is applied through the colour manager",
                    section.Name, profile);
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
        _log.LogInformation("started the input method: {Command}", command);
    }
}
