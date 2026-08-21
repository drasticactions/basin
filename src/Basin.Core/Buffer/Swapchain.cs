namespace Basin;

public sealed class Swapchain : IDisposable
{
    private const int Capacity = 3;

    private readonly IAllocator _allocator;
    private readonly Slot?[] _slots = new Slot?[Capacity];
    private readonly ulong[] _modifiers;

    public Swapchain(IAllocator allocator, int width, int height, DrmFormat format, ReadOnlySpan<ulong> modifiers)
    {
        _allocator = allocator;
        Width = width;
        Height = height;
        Format = format;
        _modifiers = modifiers.ToArray();
    }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public DrmFormat Format { get; }

    public IBuffer? Acquire(out int age)
    {
        for (var i = 0; i < _slots.Length; i++)
        {
            var slot = _slots[i];
            if (slot is null)
            {
                var buffer = _allocator.Allocate(Width, Height, Format, _modifiers, BufferUse.Render | BufferUse.Scanout);
                if (buffer is null)
                {
                    age = 0;
                    return null;
                }

                _slots[i] = new Slot(buffer);
                age = 0;
                return buffer;
            }

            if (slot.Buffer.LockCount == 0)
            {
                age = slot.Age;
                return slot.Buffer;
            }
        }

        age = 0;
        return null;
    }

    public void Presented(IBuffer buffer)
    {
        foreach (var slot in _slots)
        {
            if (slot is null)
            {
                continue;
            }

            if (slot.Buffer == buffer)
            {
                slot.Age = 1;
            }
            else if (slot.Age > 0)
            {
                slot.Age++;
            }
        }
    }

    public void Resize(int width, int height)
    {
        if (width == Width && height == Height)
        {
            return;
        }

        Width = width;
        Height = height;
        DropSlots();
    }

    public void Dispose() => DropSlots();

    private void DropSlots()
    {
        for (var i = 0; i < _slots.Length; i++)
        {
            if (_slots[i] is { } slot)
            {
                (slot.Buffer as BufferBase)?.Destroy();
                _slots[i] = null;
            }
        }
    }

    private sealed class Slot(IBuffer buffer)
    {
        public IBuffer Buffer { get; } = buffer;

        public int Age { get; set; }
    }
}
