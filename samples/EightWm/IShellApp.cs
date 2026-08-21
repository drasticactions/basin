using Basin;

namespace EightWm;

internal interface IShellApp
{
    int MinWidth { get; }

    void Placed(in Box cell);

    void Hidden();
}
