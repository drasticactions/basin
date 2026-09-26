using System.Text.Json.Serialization;

namespace Basin.Ipc;

public sealed class IpcApprovalAnswerParams : IIpcParams, IIpcReusable
{
    private long _id;
    private string _answer = string.Empty;

    private int _present;

    public IpcApprovalAnswerParams()
    {
    }

    public IpcApprovalAnswerParams(long id, string answer)
    {
        Id = id;
        Answer = answer;
    }

    public long Id
    {
        get => _id;
        set
        {
            _id = value;
            _present |= 1;
        }
    }

    public string Answer
    {
        get => _answer;
        set
        {
            _answer = value;
            _present |= value is null ? 0 : 2;
        }
    }

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
