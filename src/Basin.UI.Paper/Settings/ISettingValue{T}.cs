namespace Basin.UI.Paper;

public interface ISettingValue<T>
{
    T Value { get; set; }

    bool IsDirty { get; }

    string? Badge { get; }

    string? Error { get; }

    string? Hint => null;
}
