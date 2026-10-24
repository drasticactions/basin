using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The params of <c>windows/wait</c>. The request must name an <c>app_id</c>, a <c>title</c> or both, or the method answers <c>invalid_params</c>.
/// </summary>
public sealed class IpcWaitParams : IIpcParams, IIpcReusable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IpcWaitParams"/> class.
    /// Every key starts absent.
    /// </summary>
    public IpcWaitParams()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcWaitParams"/> class.
    /// A <see langword="null"/> argument leaves its key out of the request.
    /// </summary>
    /// <param name="appId">The exact application id to match.</param>
    /// <param name="title">The text that the title must contain.</param>
    /// <param name="timeoutMs">The time limit in milliseconds.</param>
    public IpcWaitParams(string? appId = null, string? title = null, long? timeoutMs = null)
    {
        AppId = appId;
        Title = title;
        TimeoutMs = timeoutMs;
    }

    /// <summary>
    /// Gets or sets the application id that a window must have. It matches exactly. Absent matches any application id.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AppId { get; set; }

    /// <summary>
    /// Gets or sets the text that the title of a window must contain. Absent matches any title.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Title { get; set; }

    /// <summary>
    /// Gets or sets the time in milliseconds after which the method answers <c>failed</c>, from 1 to 3600000.
    /// When it is absent, the limit is 5000 ms.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? TimeoutMs { get; set; }

    /// <summary>
    /// Gets the error when neither <see cref="AppId"/> nor <see cref="Title"/> is present, or <see langword="null"/> when one of them is.
    /// </summary>
    [JsonIgnore]
    public string? Missing => AppId is null && Title is null ? "Give an 'app_id', a 'title', or both." : null;

    void IIpcReusable.Reset()
    {
        AppId = null;
        Title = null;
        TimeoutMs = null;
    }
}
