using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Basin.Ipc;

internal static class IpcParamsReuse
{
    private static readonly List<Lease> Leased = [];
    private static int _depth;

    public static JsonSerializerOptions Options { get; } = new(IpcJsonContext.Default.Options)
    {
        TypeInfoResolver = IpcJsonContext.Default.WithAddedModifier(Modify),
    };

    public static JsonTypeInfo<T> Resolve<T>(JsonTypeInfo<T> info) =>
        ReferenceEquals(info.Options, IpcJsonContext.Default.Options) && info.Type.IsAssignableTo(typeof(IIpcReusable))
            ? (JsonTypeInfo<T>)Options.GetTypeInfo(typeof(T))
            : info;

    public static void Enter() => _depth++;

    public static void Exit()
    {
        if (--_depth > 0)
        {
            return;
        }

        foreach (var lease in Leased)
        {
            lease.InUse = false;
        }

        Leased.Clear();
    }

    private static void Modify(JsonTypeInfo info)
    {
        if (info.Kind == JsonTypeInfoKind.Object && info.Type.IsAssignableTo(typeof(IIpcReusable)) && info.CreateObject is { } create)
        {
            info.CreateObject = new Lease(create).Create;
        }
    }

    private sealed class Lease(Func<object> create)
    {
        private object? _shared;

        public bool InUse { get; set; }

        public object Create()
        {
            if (InUse || _depth == 0)
            {
                return create();
            }

            _shared ??= create();
            ((IIpcReusable)_shared).Reset();
            InUse = true;
            Leased.Add(this);
            return _shared;
        }
    }
}
