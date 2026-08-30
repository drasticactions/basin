using System.Runtime.InteropServices;

namespace Basin.Rashader;

internal static unsafe class RashaderError
{
    internal static string? Consume(nint error)
    {
        if (error == 0)
        {
            return null;
        }

        var message = "librashader reported an error it could not describe";
        byte* text = null;
        if (RashaderNative.libra_error_write(error, &text) == 0 && text is not null)
        {
            message = Marshal.PtrToStringUTF8((nint)text) ?? message;
            _ = RashaderNative.libra_error_free_string(&text);
        }

        _ = RashaderNative.libra_error_free(&error);
        return message;
    }
}
