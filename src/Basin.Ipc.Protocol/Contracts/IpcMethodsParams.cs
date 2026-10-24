using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The params of <c>ipc/methods</c>. Every key is optional.
/// </summary>
public sealed class IpcMethodsParams : IIpcParams, IIpcReusable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IpcMethodsParams"/> class.
    /// Every key starts absent.
    /// </summary>
    public IpcMethodsParams()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcMethodsParams"/> class.
    /// A <see langword="null"/> argument leaves its key out of the request.
    /// </summary>
    /// <param name="detail">Whether the reply is an <see cref="IpcMethodDetailList"/>.</param>
    public IpcMethodsParams(bool? detail = null)
    {
        Detail = detail;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the reply describes each method.
    /// With <see langword="true"/> the reply is an <see cref="IpcMethodDetailList"/>. Otherwise it is an <see cref="IpcMethodList"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Detail { get; set; }

    /// <summary>
    /// Gets <see langword="null"/> at all times, because this request has no required key.
    /// </summary>
    [JsonIgnore]
    public string? Missing => null;

    void IIpcReusable.Reset()
    {
        Detail = null;
    }
}
