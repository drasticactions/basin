using System.Runtime.InteropServices;

namespace Basin.Session;

internal static unsafe partial class PamNative
{
    public const int Success = 0;
    public const int PromptEchoOff = 1;
    public const int PromptEchoOn = 2;
    public const int ConversationError = 19;

    private const string Pam = "libpam.so.0";
    private const string C = "libc";

    [StructLayout(LayoutKind.Sequential)]
    public struct Conversation
    {
        public delegate* unmanaged<int, Message**, Response**, void*, int> Callback;
        public void* AppData;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Message
    {
        public int Style;
        public byte* Text;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Response
    {
        public byte* Text;
        public int Code;
    }

    [DllImport(Pam, EntryPoint = "pam_start")]
    public static extern int Start(byte* service, byte* user, Conversation* conversation, out nint handle);

    [DllImport(Pam, EntryPoint = "pam_authenticate")]
    public static extern int Authenticate(nint handle, int flags);

    [DllImport(Pam, EntryPoint = "pam_acct_mgmt")]
    public static extern int AccountManagement(nint handle, int flags);

    [DllImport(Pam, EntryPoint = "pam_end")]
    public static extern int End(nint handle, int status);

    [DllImport(C, EntryPoint = "calloc")]
    public static extern void* Calloc(nuint count, nuint size);

    [DllImport(C, EntryPoint = "strdup")]
    public static extern byte* Strdup(byte* text);

    [DllImport(C, EntryPoint = "pipe2", SetLastError = true)]
    public static extern int Pipe2(int* fds, int flags);

    [DllImport(C, EntryPoint = "close")]
    public static extern int Close(int fd);

    [DllImport(C, EntryPoint = "read", SetLastError = true)]
    public static extern nint Read(int fd, byte* buffer, nuint count);

    [DllImport(C, EntryPoint = "write", SetLastError = true)]
    public static extern nint Write(int fd, byte* buffer, nuint count);

    [DllImport(C, EntryPoint = "waitpid")]
    public static extern int WaitPid(int pid, int* status, int options);

    [DllImport(C, EntryPoint = "kill")]
    public static extern int Kill(int pid, int signal);

    [DllImport(C, EntryPoint = "posix_spawn_file_actions_init")]
    public static extern int FileActionsInit(nint actions);

    [DllImport(C, EntryPoint = "posix_spawn_file_actions_adddup2")]
    public static extern int FileActionsDup2(nint actions, int fd, int target);

    [DllImport(C, EntryPoint = "posix_spawn_file_actions_destroy")]
    public static extern int FileActionsDestroy(nint actions);

    [DllImport(C, EntryPoint = "posix_spawnp")]
    public static extern int SpawnP(out int pid, byte* file, nint actions, nint attributes, byte** argv, byte** envp);
}
