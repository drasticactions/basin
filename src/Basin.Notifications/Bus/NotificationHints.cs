using Tmds.DBus.Protocol;
using static Basin.Notifications.NotificationsLog;

namespace Basin.Notifications;

internal static class NotificationHints
{
    private const string ImageSignature = "(iiibiiay)";

    public static Notification Read(ref Reader reader, Notification notification)
    {
        NotificationImage? imageData = null;
        NotificationImage? imageDataLegacy = null;
        NotificationImage? iconData = null;
        string? imagePath = null;
        string? imagePathLegacy = null;
        var end = reader.ReadDictionaryStart();
        while (reader.HasNext(end))
        {
            var key = reader.ReadString();
            var peek = reader;
            var signature = peek.ReadSignatureAsString();
            switch (key)
            {
                case "urgency":
                    var urgency = signature == "y" ? ReadByte(ref reader) : ReadInteger(ref reader);
                    if (urgency is >= 0 and <= 2)
                    {
                        notification = notification with { Urgency = (NotificationUrgency)urgency };
                    }

                    break;
                case "category":
                    notification = notification with { Category = ReadString(ref reader, signature) };
                    break;
                case "desktop-entry":
                    notification = notification with { DesktopEntry = ReadString(ref reader, signature) };
                    break;
                case "image-path":
                    imagePath = ReadString(ref reader, signature);
                    break;
                case "image_path":
                    imagePathLegacy = ReadString(ref reader, signature);
                    break;
                case "sound-file":
                    notification = notification with { SoundFile = ReadString(ref reader, signature) };
                    break;
                case "sound-name":
                    notification = notification with { SoundName = ReadString(ref reader, signature) };
                    break;
                case "suppress-sound":
                    notification = notification with { SuppressSound = ReadBool(ref reader, signature) };
                    break;
                case "resident":
                    notification = notification with { Resident = ReadBool(ref reader, signature) };
                    break;
                case "transient":
                    notification = notification with { Transient = ReadBool(ref reader, signature) };
                    break;
                case "action-icons":
                    notification = notification with { ActionIcons = ReadBool(ref reader, signature) };
                    break;
                case "image-data":
                    imageData = ReadImage(ref reader, signature);
                    break;
                case "image_data":
                    imageDataLegacy = ReadImage(ref reader, signature);
                    break;
                case "icon_data":
                    iconData = ReadImage(ref reader, signature);
                    break;
                default:
                    reader.ReadVariantValue();
                    break;
            }
        }

        return notification with
        {
            Image = imageData ?? imageDataLegacy ?? iconData,
            ImagePath = string.IsNullOrEmpty(imagePath) ? (string.IsNullOrEmpty(imagePathLegacy) ? null : imagePathLegacy) : imagePath,
        };
    }

    private static string? ReadString(ref Reader reader, string signature)
    {
        if (signature != "s")
        {
            reader.ReadVariantValue();
            return null;
        }

        reader.ReadSignature("s");
        var value = reader.ReadString();
        return value.Length == 0 ? null : value;
    }

    private static bool ReadBool(ref Reader reader, string signature)
    {
        if (signature == "b")
        {
            reader.ReadSignature("b");
            return reader.ReadBool();
        }

        return ReadInteger(ref reader) > 0;
    }

    private static long ReadByte(ref Reader reader)
    {
        reader.ReadSignature("y");
        return reader.ReadByte();
    }

    private static long ReadInteger(ref Reader reader)
    {
        var value = reader.ReadVariantValue();
        return value.Type switch
        {
            VariantValueType.Byte => value.GetByte(),
            VariantValueType.Bool => value.GetBool() ? 1 : 0,
            VariantValueType.Int16 => value.GetInt16(),
            VariantValueType.UInt16 => value.GetUInt16(),
            VariantValueType.Int32 => value.GetInt32(),
            VariantValueType.UInt32 => value.GetUInt32(),
            VariantValueType.Int64 => value.GetInt64(),
            VariantValueType.UInt64 => (long)Math.Min(value.GetUInt64(), long.MaxValue),
            _ => -1,
        };
    }

    private static NotificationImage? ReadImage(ref Reader reader, string signature)
    {
        if (signature != ImageSignature)
        {
            reader.ReadVariantValue();
            Log.Debug($"hints: image hint signature {signature} is not {ImageSignature}, hint ignored");
            return null;
        }

        reader.ReadSignature(ImageSignature);
        reader.AlignStruct();
        var width = reader.ReadInt32();
        var height = reader.ReadInt32();
        var stride = reader.ReadInt32();
        var alpha = reader.ReadBool();
        var bits = reader.ReadInt32();
        var channels = reader.ReadInt32();
        var pixels = reader.ReadArrayOfByte();
        if (!Fits(width, height, stride, bits, channels, pixels.Length))
        {
            Log.Debug($"hints: image hint {width}x{height}, stride {stride}, {channels}x{bits} bits does not fit {pixels.Length} bytes, hint ignored");
            return null;
        }

        return new NotificationImage(width, height, stride, alpha, bits, channels, pixels);
    }

    private static bool Fits(int width, int height, int stride, int bits, int channels, int length)
    {
        if (width <= 0 || height <= 0 || bits <= 0 || channels <= 0 || bits > 32 || channels > 4)
        {
            return false;
        }

        var row = ((long)width * channels * bits + 7) / 8;
        return stride >= row && (long)stride * (height - 1) + row <= length;
    }
}
