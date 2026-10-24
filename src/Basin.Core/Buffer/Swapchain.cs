using Basin.Diagnostics;

namespace Basin;

/// <summary>
/// Up to three render and scanout buffers that it allocates on demand and reuses once their locks are gone. It tracks each buffer's age for damage tracking.
/// </summary>
public sealed class Swapchain : IDisposable
{
    private const int Capacity = 3;

    private readonly IAllocator _allocator;
    private readonly Slot?[] _slots = new Slot?[Capacity];
    private readonly ulong[] _modifiers;

    /// <summary>Initializes a new instance of the <see cref="Swapchain"/> class. It allocates no buffer until the first <see cref="Acquire"/>.</summary>
    /// <param name="allocator">The allocator for new buffers.</param>
    /// <param name="width">The buffer width in physical pixels.</param>
    /// <param name="height">The buffer height in physical pixels.</param>
    /// <param name="format">The pixel format.</param>
    /// <param name="modifiers">The modifiers that the caller accepts. The swapchain copies the span.</param>
    public Swapchain(IAllocator allocator, int width, int height, DrmFormat format, ReadOnlySpan<ulong> modifiers)
    {
        _allocator = allocator;
        Width = width;
        Height = height;
        Format = format;
        _modifiers = modifiers.ToArray();
    }

    /// <summary>Gets the buffer width in physical pixels.</summary>
    public int Width { get; private set; }

    /// <summary>Gets the buffer height in physical pixels.</summary>
    public int Height { get; private set; }

    /// <summary>Gets the pixel format of every buffer.</summary>
    public DrmFormat Format { get; }

    /// <summary>
    /// Returns a buffer with no locks, or allocates one in an empty slot. The caller must lock the buffer while it uses it.
    /// </summary>
    /// <param name="age">The number of presents since the buffer's content was shown, or zero when the content is undefined.</param>
    /// <returns>A free buffer, or <see langword="null"/> when all three slots are locked or the allocation fails.</returns>
    public IBuffer? Acquire(out int age)
    {
        for (var i = 0; i < _slots.Length; i++)
        {
            var slot = _slots[i];
            if (slot is null)
            {
                AllocationScope.Pause();
                var buffer = _allocator.Allocate(Width, Height, Format, _modifiers, BufferUse.Render | BufferUse.Scanout);
                var made = buffer is null ? null : new Slot(buffer);
                AllocationScope.Resume();
                if (made is null)
                {
                    age = 0;
                    return null;
                }

                _slots[i] = made;
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

    /// <summary>
    /// Records a present. The presented buffer gets age 1 and each other buffer with content ages by one.
    /// </summary>
    /// <param name="buffer">The buffer that was presented.</param>
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

    /// <summary>
    /// Changes the buffer size. A new size destroys every buffer, and the next <see cref="Acquire"/> allocates again.
    /// </summary>
    /// <param name="width">The new width in physical pixels.</param>
    /// <param name="height">The new height in physical pixels.</param>
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

    /// <summary>
    /// Destroys every buffer that the swapchain allocated.
    /// </summary>
    public void Dispose() => DropSlots();

    private void DropSlots()
    {
        for (var i = 0; i < _slots.Length; i++)
        {
            if (_slots[i] is { } slot)
            {
                slot.Buffer.Destroy();
                _slots[i] = null;
            }
        }
    }

    private sealed class Slot(BufferBase buffer)
    {
        public BufferBase Buffer { get; } = buffer;

        public int Age { get; set; }
    }
}
