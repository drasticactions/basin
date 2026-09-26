using System.Text.Json.Serialization;

namespace Basin.Ipc;

public sealed class IpcMethodsParams : IIpcParams, IIpcReusable
{
    public IpcMethodsParams()
    {
    }

    public IpcMethodsParams(bool? detail = null)
    {
        Detail = detail;
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Detail { get; set; }

    [JsonIgnore]
    public string? Missing => null;

    void IIpcReusable.Reset()
    {
        Detail = null;
    }
}
