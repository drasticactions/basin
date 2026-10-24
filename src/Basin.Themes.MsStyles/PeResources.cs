using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Text;

namespace Basin.Themes.MsStyles;

/// <summary>
/// Reads the resource tree of a PE image, for example an <c>.msstyles</c> file. Each resource is keyed by type and name, and the first language entry wins.
/// </summary>
public sealed class PeResources
{
    /// <summary>
    /// The resource type ID of a string table, <c>RT_STRING</c>.
    /// </summary>
    public const int StringType = 6;

    /// <summary>
    /// The resource type ID of a bitmap, <c>RT_BITMAP</c>.
    /// </summary>
    public const int BitmapType = 2;

    private const int MaxEntries = 65536;
    private const int DirectoryHeaderSize = 16;
    private const int EntrySize = 8;
    private const int DataEntrySize = 16;

    private readonly ReadOnlyMemory<byte> _file;
    private readonly Dictionary<PeResourceKey, Dictionary<PeResourceKey, ReadOnlyMemory<byte>>> _types = [];
    private readonly List<PeResourceKey> _typeOrder = [];
    private readonly Dictionary<PeResourceKey, List<PeResourceKey>> _nameOrder = [];
    private SectionHeader[] _sections = [];
    private int _resourceStart;
    private int _resourceEnd;
    private int _entries;

    private PeResources(ReadOnlyMemory<byte> file)
    {
        _file = file;
    }

    /// <summary>
    /// Gets the bytes of the whole image. Every resource that this object returns is a slice of these bytes.
    /// </summary>
    public ReadOnlyMemory<byte> File => _file;

    /// <summary>
    /// Gets the resource types in the order of the resource directory.
    /// </summary>
    public IReadOnlyList<PeResourceKey> Types => _typeOrder;

    /// <summary>
    /// Reads the resource tree of a PE image. The result keeps a reference to <paramref name="file"/> and does not copy it.
    /// </summary>
    /// <param name="file">The bytes of the image.</param>
    /// <returns>The resources of the image.</returns>
    /// <exception cref="MsStylesFormatException">The bytes are not a PE image, the image has no resource directory, or the tree is damaged.</exception>
    public static PeResources Read(ReadOnlyMemory<byte> file)
    {
        var resources = new PeResources(file);
        resources.Load();
        return resources;
    }

    /// <summary>
    /// Lists the resource names of one type.
    /// </summary>
    /// <param name="type">The resource type.</param>
    /// <returns>The names in directory order. The list is empty when the image has no resource of the type.</returns>
    public IReadOnlyList<PeResourceKey> Names(PeResourceKey type) =>
        _nameOrder.TryGetValue(type, out var names) ? names : [];

    /// <summary>
    /// Checks whether the image has a resource.
    /// </summary>
    /// <param name="type">The resource type.</param>
    /// <param name="name">The resource name.</param>
    /// <returns><see langword="true"/> when the resource exists.</returns>
    public bool Contains(PeResourceKey type, PeResourceKey name) =>
        _types.TryGetValue(type, out var names) && names.ContainsKey(name);

    /// <summary>
    /// Finds the data of a resource.
    /// </summary>
    /// <param name="type">The resource type.</param>
    /// <param name="name">The resource name.</param>
    /// <param name="data">A slice of <see cref="File"/> that holds the data, or empty when the resource does not exist.</param>
    /// <returns><see langword="true"/> when the resource exists.</returns>
    public bool TryFind(PeResourceKey type, PeResourceKey name, out ReadOnlyMemory<byte> data)
    {
        if (_types.TryGetValue(type, out var names) && names.TryGetValue(name, out data))
        {
            return true;
        }

        data = default;
        return false;
    }

    /// <summary>
    /// Reads one string from the string tables, as <c>LoadString</c> does.
    /// </summary>
    /// <param name="id">The string ID.</param>
    /// <param name="value">The string, or empty when it is not found.</param>
    /// <returns><see langword="true"/> when the string exists and is not empty. It is <see langword="false"/> for a negative ID.</returns>
    public bool TryGetString(int id, out string value)
    {
        value = string.Empty;
        if (id < 0 || !TryFind(StringType, id / 16 + 1, out var block))
        {
            return false;
        }

        var span = block.Span;
        var position = 0;
        for (var i = 0; i < 16; i++)
        {
            if (position + 2 > span.Length)
            {
                return false;
            }

            var length = BinaryPrimitives.ReadUInt16LittleEndian(span[position..]);
            position += 2;
            if (position + length * 2 > span.Length)
            {
                return false;
            }

            if (i == id % 16)
            {
                if (length == 0)
                {
                    return false;
                }

                value = Encoding.Unicode.GetString(span.Slice(position, length * 2));
                return true;
            }

            position += length * 2;
        }

        return false;
    }

    private void Load()
    {
        PEHeaders headers;
        try
        {
            using var stream = new MemoryStreamOver(_file);
            headers = new PEHeaders(stream);
        }
        catch (BadImageFormatException e)
        {
            throw new MsStylesFormatException("Not a PE image: " + e.Message, e);
        }

        if (headers.PEHeader is not { } pe)
        {
            throw new MsStylesFormatException("PE image has no optional header.", 0);
        }

        _sections = [.. headers.SectionHeaders];
        var directory = pe.ResourceTableDirectory;
        if (directory.RelativeVirtualAddress == 0 || directory.Size == 0)
        {
            throw new MsStylesFormatException("PE image has no resource directory.", 0);
        }

        _resourceStart = RvaToOffset(directory.RelativeVirtualAddress, directory.Size);
        _resourceEnd = _resourceStart + directory.Size;
        var section = SectionOf(directory.RelativeVirtualAddress);
        if (section is { } containing)
        {
            var sectionEnd = containing.PointerToRawData + containing.SizeOfRawData;
            _resourceEnd = Math.Min(Math.Max(_resourceEnd, sectionEnd), _file.Length);
        }

        var visited = new HashSet<int>();
        foreach (var (typeKey, typeTarget, typeIsDirectory) in Entries(0, visited))
        {
            if (!typeIsDirectory)
            {
                continue;
            }

            if (!_types.TryGetValue(typeKey, out var names))
            {
                names = [];
                _types[typeKey] = names;
                _typeOrder.Add(typeKey);
                _nameOrder[typeKey] = [];
            }

            foreach (var (nameKey, nameTarget, nameIsDirectory) in Entries(typeTarget, visited))
            {
                var dataEntry = nameTarget;
                if (nameIsDirectory)
                {
                    dataEntry = -1;
                    foreach (var (_, languageTarget, languageIsDirectory) in Entries(nameTarget, visited))
                    {
                        if (languageIsDirectory)
                        {
                            throw new MsStylesFormatException("Resource language entry points to a directory.", _resourceStart + languageTarget);
                        }

                        dataEntry = languageTarget;
                        break;
                    }

                    if (dataEntry < 0)
                    {
                        continue;
                    }
                }

                if (names.ContainsKey(nameKey))
                {
                    continue;
                }

                names[nameKey] = Data(dataEntry);
                _nameOrder[typeKey].Add(nameKey);
            }
        }
    }

    private List<(PeResourceKey Key, int Target, bool IsDirectory)> Entries(int directoryOffset, HashSet<int> visited)
    {
        if (!visited.Add(directoryOffset))
        {
            throw new MsStylesFormatException("Resource directory cycle.", _resourceStart + directoryOffset);
        }

        var at = Check(directoryOffset, DirectoryHeaderSize);
        var span = _file.Span;
        var named = BinaryPrimitives.ReadUInt16LittleEndian(span[(at + 12)..]);
        var ids = BinaryPrimitives.ReadUInt16LittleEndian(span[(at + 14)..]);
        var count = named + ids;
        _entries += count;
        if (_entries > MaxEntries)
        {
            throw new MsStylesFormatException($"Resource tree has more than {MaxEntries} entries.", at);
        }

        Check(directoryOffset + DirectoryHeaderSize, count * EntrySize);
        var entries = new List<(PeResourceKey, int, bool)>(count);
        for (var i = 0; i < count; i++)
        {
            var entry = at + DirectoryHeaderSize + i * EntrySize;
            var name = BinaryPrimitives.ReadUInt32LittleEndian(span[entry..]);
            var target = BinaryPrimitives.ReadUInt32LittleEndian(span[(entry + 4)..]);
            PeResourceKey key;
            if ((name & 0x8000_0000) != 0)
            {
                var stringOffset = (int)(name & 0x7fff_ffff);
                var stringAt = Check(stringOffset, 2);
                var length = BinaryPrimitives.ReadUInt16LittleEndian(span[stringAt..]);
                Check(stringOffset + 2, length * 2);
                key = new PeResourceKey(Encoding.Unicode.GetString(span.Slice(stringAt + 2, length * 2)));
            }
            else
            {
                key = new PeResourceKey((int)(name & 0xffff));
            }

            var isDirectory = (target & 0x8000_0000) != 0;
            entries.Add((key, (int)(target & 0x7fff_ffff), isDirectory));
        }

        return entries;
    }

    private ReadOnlyMemory<byte> Data(int dataEntryOffset)
    {
        var at = Check(dataEntryOffset, DataEntrySize);
        var span = _file.Span;
        var rva = BinaryPrimitives.ReadInt32LittleEndian(span[at..]);
        var size = BinaryPrimitives.ReadInt32LittleEndian(span[(at + 4)..]);
        if (size < 0)
        {
            throw new MsStylesFormatException("Resource data has a negative size.", at);
        }

        var offset = RvaToOffset(rva, size);
        return _file.Slice(offset, size);
    }

    private int Check(int relative, int length)
    {
        var at = (long)_resourceStart + relative;
        if (relative < 0 || length < 0 || at + length > _resourceEnd)
        {
            throw new MsStylesFormatException("Resource offset out of range.", at);
        }

        return (int)at;
    }

    private int RvaToOffset(int rva, int size)
    {
        if (SectionOf(rva) is { } section)
        {
            var offset = (long)rva - section.VirtualAddress + section.PointerToRawData;
            if (offset >= 0 && size >= 0 && offset + size <= _file.Length)
            {
                return (int)offset;
            }
        }

        throw new MsStylesFormatException($"RVA 0x{rva:x} is outside every section.", rva);
    }

    private SectionHeader? SectionOf(int rva)
    {
        foreach (var section in _sections)
        {
            var extent = Math.Max(section.VirtualSize, section.SizeOfRawData);
            if (rva >= section.VirtualAddress && rva < section.VirtualAddress + extent)
            {
                return section;
            }
        }

        return null;
    }

    private sealed class MemoryStreamOver : Stream
    {
        private readonly ReadOnlyMemory<byte> _memory;
        private long _position;

        public MemoryStreamOver(ReadOnlyMemory<byte> memory)
        {
            _memory = memory;
        }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => _memory.Length;

        public override long Position
        {
            get => _position;
            set => _position = value;
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_position >= _memory.Length)
            {
                return 0;
            }

            var n = (int)Math.Min(buffer.Length, _memory.Length - _position);
            _memory.Span.Slice((int)_position, n).CopyTo(buffer);
            _position += n;
            return n;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            _position = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                _ => _memory.Length + offset,
            };
            return _position;
        }

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
