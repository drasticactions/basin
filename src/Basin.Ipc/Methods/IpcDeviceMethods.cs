using System.Globalization;
using System.Text.Json;
using Basin.Capabilities;

namespace Basin.Ipc;

internal static class IpcDeviceMethods
{
    private static readonly string[] CapabilityWords =
        ["keyboard", "pointer", "touch", "gesture", "switch", "tablet_tool", "tablet_pad"];

    public static void Register(IpcServer server, IpcDescribe describe)
    {
        if (describe.Find<IInputDeviceConfiguration>() is not { } configuration)
        {
            return;
        }

        var sources = configuration as IInputSettingSources;
        var methods = server.Methods;
        methods.RegisterLibrary(IpcMethodNames.DevicesList, (ref IpcParams _, IpcReply reply) =>
        {
            var devices = Enumerate(configuration);
            var list = new IpcDevice[devices.Length];
            for (var i = 0; i < devices.Length; i++)
            {
                list[i] = Describe(devices[i], sources);
            }

            reply.Write(new IpcDeviceList(list), IpcJsonContext.Default.IpcDeviceList);
        });

        methods.RegisterLibrary(IpcMethodNames.DevicesGet, (ref IpcParams parameters, IpcReply reply) =>
        {
            if (parameters.Read(IpcJsonContext.Default.IpcDeviceParams) is not { } request)
            {
                return;
            }

            if (Find(configuration, request.Device) is not { } device)
            {
                reply.Error(IpcErrorCodes.NotFound, $"No input device has the id {request.Device.ToString(CultureInfo.InvariantCulture)}. Call devices/list for the devices.");
                return;
            }

            reply.Write(Settings(configuration, sources, device), IpcJsonContext.Default.IpcDeviceSettings);
        });

        methods.RegisterLibrary(IpcMethodNames.DevicesSet, (ref IpcParams parameters, IpcReply reply) =>
        {
            if (parameters.Read(IpcJsonContext.Default.IpcDeviceSetParams) is not { } request)
            {
                return;
            }

            var targets = new List<InputDeviceInfo>();
            if (request.Device is { } id)
            {
                if (Find(configuration, id) is not { } device)
                {
                    reply.Error(IpcErrorCodes.NotFound, $"No input device has the id {id.ToString(CultureInfo.InvariantCulture)}. Call devices/list for the devices.");
                    return;
                }

                targets.Add(device);
            }
            else if (!InputSettingNames.TryParseType(request.Type!, out var type))
            {
                reply.Error(IpcErrorCodes.InvalidParams, $"'{request.Type}' is not a device type.");
                return;
            }
            else
            {
                foreach (var device in Enumerate(configuration))
                {
                    if (OfType(device, type))
                    {
                        targets.Add(device);
                    }
                }
            }

            var outcomes = new List<IpcDeviceOutcome>();
            foreach (var (key, raw) in request.Settings!)
            {
                var known = InputSettingNames.TryParseKey(key, out var setting);
                var parsed = default(InputSettingValue);
                var valid = known && Text(raw) is { } text && InputSettingNames.TryParse(setting, text, out parsed);
                if (targets.Count == 0)
                {
                    outcomes.Add(new IpcDeviceOutcome(0, key, Word(valid ? InputSettingResult.Unsupported : InputSettingResult.Invalid)));
                }

                foreach (var device in targets)
                {
                    var result = valid ? configuration.Set(device.Id, setting, parsed) : InputSettingResult.Invalid;
                    outcomes.Add(new IpcDeviceOutcome(device.Id, key, Word(result)));
                }
            }

            reply.Write(new IpcDeviceResult(outcomes.ToArray()), IpcJsonContext.Default.IpcDeviceResult);
        });

        methods.RegisterLibrary(IpcMethodNames.DevicesReset, (ref IpcParams parameters, IpcReply reply) =>
        {
            if (parameters.Read(IpcJsonContext.Default.IpcDeviceResetParams) is not { } request)
            {
                return;
            }

            if (Find(configuration, request.Device) is not { } device)
            {
                reply.Error(IpcErrorCodes.NotFound, $"No input device has the id {request.Device.ToString(CultureInfo.InvariantCulture)}. Call devices/list for the devices.");
                return;
            }

            var outcomes = new List<IpcDeviceOutcome>();
            if (request.Keys is { } keys)
            {
                foreach (var key in keys)
                {
                    var result = InputSettingNames.TryParseKey(key, out var setting)
                        ? configuration.Reset(device.Id, setting)
                        : InputSettingResult.Invalid;
                    outcomes.Add(new IpcDeviceOutcome(device.Id, key, Word(result)));
                }
            }
            else
            {
                foreach (var setting in InputSettingNames.Settings)
                {
                    var runtime = sources is not null
                        ? sources.SourceOf(device.Id, setting) == InputSettingLayer.Runtime
                        : configuration.Support(device.Id, setting).Supported;
                    if (runtime)
                    {
                        outcomes.Add(new IpcDeviceOutcome(device.Id, InputSettingNames.Key(setting), Word(configuration.Reset(device.Id, setting))));
                    }
                }
            }

            reply.Write(new IpcDeviceResult(outcomes.ToArray()), IpcJsonContext.Default.IpcDeviceResult);
        });
    }

    internal static InputDeviceInfo[] Enumerate(IInputDeviceConfiguration configuration)
    {
        var devices = new InputDeviceInfo[16];
        var count = configuration.Enumerate(devices);
        while (count < 0)
        {
            devices = new InputDeviceInfo[devices.Length * 2];
            count = configuration.Enumerate(devices);
        }

        return devices.AsSpan(0, count).ToArray();
    }

    internal static InputDeviceInfo? Find(IInputDeviceConfiguration configuration, ulong id)
    {
        foreach (var device in Enumerate(configuration))
        {
            if (device.Id == id)
            {
                return device;
            }
        }

        return null;
    }

    internal static IpcDevice Describe(in InputDeviceInfo device, IInputSettingSources? sources)
    {
        var capabilities = new List<string>(CapabilityWords.Length);
        for (var bit = 0; bit < CapabilityWords.Length; bit++)
        {
            if (((int)device.Capabilities & (1 << bit)) != 0)
            {
                capabilities.Add(CapabilityWords[bit]);
            }
        }

        var rules = sources?.RulesFor(device.Id) is { Count: > 0 } matched ? matched.ToArray() : [];
        return new IpcDevice(
            device.Id,
            device.Name,
            InputSettingNames.TypeName(device.Type),
            capabilities.ToArray(),
            device.Sysname,
            device.Vendor,
            device.Product,
            device.Bus,
            device.OutputName,
            rules);
    }

    internal static string Format(IInputDeviceConfiguration configuration, ulong device, InputSetting setting) =>
        configuration.TryGet(device, setting, out var value) ? InputSettingNames.Format(setting, value) : string.Empty;

    private static IpcDeviceSettings Settings(IInputDeviceConfiguration configuration, IInputSettingSources? sources, in InputDeviceInfo device)
    {
        var settings = new List<IpcDeviceSetting>();
        foreach (var setting in InputSettingNames.Settings)
        {
            var support = configuration.Support(device.Id, setting);
            if (!support.Supported)
            {
                continue;
            }

            var current = configuration.TryGet(device.Id, setting, out var now) ? InputSettingNames.Format(setting, now) : string.Empty;
            var fallback = configuration.TryGetDefault(device.Id, setting, out var initial) ? InputSettingNames.Format(setting, initial) : string.Empty;
            var layer = sources?.SourceOf(device.Id, setting)
                ?? (current == fallback ? InputSettingLayer.Default : InputSettingLayer.Runtime);
            var words = new List<string>();
            foreach (var word in InputSettingNames.Words(setting))
            {
                if (InputSettingNames.IsSupportedWord(setting, word, support))
                {
                    words.Add(word);
                }
            }

            settings.Add(new IpcDeviceSetting(InputSettingNames.Key(setting), current, fallback, InputSettingNames.LayerName(layer), words.ToArray()));
        }

        return new IpcDeviceSettings(device.Id, device.Name, settings.ToArray());
    }

    private static bool OfType(in InputDeviceInfo device, InputDeviceType type) =>
        type == InputDeviceType.Keyboard
            ? (device.Capabilities & InputDeviceCapability.Keyboard) != 0
            : device.Type == type;

    private static string? Text(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.Array => Numbers(value),
        _ => null,
    };

    private static string? Numbers(JsonElement array)
    {
        var parts = new List<string>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number)
            {
                return null;
            }

            parts.Add(item.GetRawText());
        }

        return string.Join(' ', parts);
    }

    private static string Word(InputSettingResult result) => result switch
    {
        InputSettingResult.Success => "ok",
        InputSettingResult.Unsupported => "unsupported",
        _ => "invalid",
    };
}
