using System.Text.Json.Serialization;

namespace Basin.Ipc;

/// <summary>
/// The params of <c>approval/answer</c>: the answer to one pending approval request.
/// </summary>
public sealed class IpcApprovalAnswerParams : IIpcParams, IIpcReusable
{
    private long _id;
    private string _answer = string.Empty;

    private int _present;

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcApprovalAnswerParams"/> class. Every key starts absent, so the deserializer can fill it.
    /// </summary>
    public IpcApprovalAnswerParams()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpcApprovalAnswerParams"/> class. Both keys are present.
    /// </summary>
    /// <param name="id">The request id from the approval event.</param>
    /// <param name="answer"><c>allow_once</c>, <c>allow_run</c> or <c>deny</c>.</param>
    public IpcApprovalAnswerParams(long id, string answer)
    {
        Id = id;
        Answer = answer;
    }

    /// <summary>
    /// Gets or sets the id of the request, as the <c>approval/requested</c> event carries it. The key is required.
    /// </summary>
    public long Id
    {
        get => _id;
        set
        {
            _id = value;
            _present |= 1;
        }
    }

    /// <summary>
    /// Gets or sets the answer: <c>allow_once</c> runs the held call, <c>allow_run</c> also allows the method for the run, and <c>deny</c> refuses it. The key is required.
    /// </summary>
    public string Answer
    {
        get => _answer;
        set
        {
            _answer = value;
            _present |= value is null ? 0 : 2;
        }
    }

    /// <inheritdoc/>
    [JsonIgnore]
    public string? Missing =>
        (_present & 1) == 0 ? "'id' is required"
        : (_present & 2) == 0 ? "'answer' is required"
        : null;

    void IIpcReusable.Reset()
    {
        _id = default;
        _answer = string.Empty;
        _present = 0;
    }
}
