using Avalonia.Controls;

namespace EightWm;

public sealed class CurtainPair : Grid
{
    public CurtainPair(CurtainModel under, CurtainModel over)
    {
        Under = under;
        Over = over;
        Children.Add(new LockCurtainView { DataContext = under });
        Children.Add(new LockCurtainView { DataContext = over });
    }

    public CurtainModel Under { get; }

    public CurtainModel Over { get; }
}
