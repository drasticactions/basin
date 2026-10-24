using System.Runtime.InteropServices;
using System.Text;

namespace Basin.Session;

/// <summary>
/// The helper side of <see cref="PamCheck"/>. A program calls it from its entry point to authenticate one user with libpam.
/// </summary>
public static unsafe class PamHelper
{
    /// <summary>
    /// Reads one password line from stdin, runs PAM authentication and account management, and writes "1" or "0" to stdout.
    /// </summary>
    /// <param name="service">The PAM service name, for example "login".</param>
    /// <param name="user">The name of the user to authenticate.</param>
    /// <returns>The process exit code: 0 on success, and 1 on failure or when libpam is not available.</returns>
    public static int Run(string service, string user)
    {
        var password = ReadLine(Console.OpenStandardInput());
        var result = Check(service, user, password);
        Array.Clear(password);
        using var output = Console.OpenStandardOutput();
        output.Write(result ? "1\n"u8 : "0\n"u8);
        output.Flush();
        return result ? 0 : 1;
    }

    private static byte[] ReadLine(Stream input)
    {
        var buffer = new byte[1024];
        var length = 0;
        while (length < buffer.Length - 1)
        {
            var read = input.Read(buffer, length, 1);
            if (read <= 0 || buffer[length] == (byte)'\n')
            {
                break;
            }

            length++;
        }

        var password = new byte[length + 1];
        buffer.AsSpan(0, length).CopyTo(password);
        Array.Clear(buffer);
        return password;
    }

    private static bool Check(string service, string user, byte[] password)
    {
        var serviceBytes = Encoding.UTF8.GetBytes(service + "\0");
        var userBytes = Encoding.UTF8.GetBytes(user + "\0");
        fixed (byte* secret = password)
        fixed (byte* serviceText = serviceBytes)
        fixed (byte* userText = userBytes)
        {
            var conversation = new PamNative.Conversation { Callback = &Converse, AppData = secret };
            nint handle;
            try
            {
                if (PamNative.Start(serviceText, userText, &conversation, out handle) != PamNative.Success)
                {
                    return false;
                }
            }
            catch (DllNotFoundException)
            {
                return false;
            }

            var status = PamNative.Authenticate(handle, 0);
            if (status == PamNative.Success)
            {
                status = PamNative.AccountManagement(handle, 0);
            }

            _ = PamNative.End(handle, status);
            return status == PamNative.Success;
        }
    }

    [UnmanagedCallersOnly]
    private static int Converse(int count, PamNative.Message** messages, PamNative.Response** responses, void* appData)
    {
        if (count <= 0 || count > 32)
        {
            return PamNative.ConversationError;
        }

        var replies = (PamNative.Response*)PamNative.Calloc((nuint)count, (nuint)sizeof(PamNative.Response));
        if (replies is null)
        {
            return PamNative.ConversationError;
        }

        for (var i = 0; i < count; i++)
        {
            var style = messages[i]->Style;
            if (style is PamNative.PromptEchoOff or PamNative.PromptEchoOn)
            {
                replies[i].Text = PamNative.Strdup((byte*)appData);
            }
        }

        *responses = replies;
        return PamNative.Success;
    }
}
