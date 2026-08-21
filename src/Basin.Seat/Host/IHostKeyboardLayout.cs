using System.Text;

namespace Basin.Seat;

public interface IHostKeyboardLayout
{
    string Name { get; }

    bool TryReadKeymapText(out string xkb);

    event Action? Changed;
}
