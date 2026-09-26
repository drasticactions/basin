namespace Basin.Ipc;

internal static class IpcSpans
{
    public static T[] Fill<T>(IpcSpanFill<T> fill, int start = 16)
    {
        var size = start;
        while (size <= 1 << 20)
        {
            var buffer = new T[size];
            var written = fill(buffer);
            if (written >= 0 && written < size)
            {
                return buffer[..written];
            }

            size *= 4;
        }

        return [];
    }
}
