namespace EightWm;

internal interface IClosable
{
    int Pid { get; }

    bool IsAttributable { get; }

    void RequestClose();
}
