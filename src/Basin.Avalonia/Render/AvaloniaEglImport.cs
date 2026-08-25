using Basin.Diagnostics;
using static Basin.Avalonia.AvaloniaLog;

namespace Basin.Avalonia;

public sealed unsafe class AvaloniaEglImport
{
    private const uint LinuxDmaBufExt = 0x3270;
    private const int AttribWidth = 0x3057;
    private const int AttribHeight = 0x3056;
    private const int AttribFourcc = 0x3271;
    private static readonly int[] PlaneFd = [0x3272, 0x3275, 0x3278, 0x3440];
    private static readonly int[] PlaneOffset = [0x3273, 0x3276, 0x3279, 0x3441];
    private static readonly int[] PlanePitch = [0x3274, 0x3277, 0x327A, 0x3442];
    private static readonly int[] PlaneModifierLo = [0x3443, 0x3445, 0x3447, 0x3449];
    private static readonly int[] PlaneModifierHi = [0x3444, 0x3446, 0x3448, 0x344A];
    private const int EglDeviceExt = 0x322C;
    private const int DrmRenderNodeFileExt = 0x3377;
    private const uint GlTexture2D = 0x0DE1;
    private const uint GlNoError = 0;

    private readonly nint _display;
    private readonly delegate* unmanaged[Cdecl]<nint, nint, uint, nint, int*, nint> _createImage;
    private readonly delegate* unmanaged[Cdecl]<nint, nint, uint> _destroyImage;
    private readonly delegate* unmanaged[Cdecl]<uint, void*, void> _imageTargetTexture;
    private readonly delegate* unmanaged[Cdecl]<nint, int, int*, int*, uint> _queryFormats;
    private readonly delegate* unmanaged[Cdecl]<nint, int, int, ulong*, uint*, int*, uint> _queryModifiers;
    private readonly delegate* unmanaged[Cdecl]<int, uint*, void> _genTextures;
    private readonly delegate* unmanaged[Cdecl]<uint, uint, void> _bindTexture;
    private readonly delegate* unmanaged[Cdecl]<int, uint*, void> _deleteTextures;
    private readonly delegate* unmanaged[Cdecl]<uint> _getError;

    private AvaloniaEglImport(nint display)
    {
        _display = display;
        _createImage = (delegate* unmanaged[Cdecl]<nint, nint, uint, nint, int*, nint>)Load("eglCreateImageKHR");
        _destroyImage = (delegate* unmanaged[Cdecl]<nint, nint, uint>)Load("eglDestroyImageKHR");
        _imageTargetTexture = (delegate* unmanaged[Cdecl]<uint, void*, void>)Load("glEGLImageTargetTexture2DOES");
        _queryFormats = (delegate* unmanaged[Cdecl]<nint, int, int*, int*, uint>)Load("eglQueryDmaBufFormatsEXT");
        _queryModifiers = (delegate* unmanaged[Cdecl]<nint, int, int, ulong*, uint*, int*, uint>)Load("eglQueryDmaBufModifiersEXT");
        _genTextures = (delegate* unmanaged[Cdecl]<int, uint*, void>)Load("glGenTextures");
        _bindTexture = (delegate* unmanaged[Cdecl]<uint, uint, void>)Load("glBindTexture");
        _deleteTextures = (delegate* unmanaged[Cdecl]<int, uint*, void>)Load("glDeleteTextures");
        _getError = (delegate* unmanaged[Cdecl]<uint>)Load("glGetError");
        Formats = QueryFormats();
        RenderNodePath = QueryRenderNode();
    }

    public static AvaloniaEglImport? TryCreate(nint eglDisplayHandle)
    {
        if (eglDisplayHandle == 0)
        {
            return null;
        }

        try
        {
            var import = new AvaloniaEglImport(eglDisplayHandle);
            return import.Formats.Count > 0 ? import : null;
        }
        catch (Exception error) when (
            error is InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            Log.Info($"dmabuf import unavailable on this display: {error.Message}");
            return null;
        }
    }

    public DrmFormatSet Formats { get; }

    public string? RenderNodePath { get; }

    public (uint Texture, nint Image)? Import(in DmabufAttributes attributes)
    {
        Span<int> attribs = stackalloc int[7 + attributes.PlaneCount * 10];
        var n = 0;
        attribs[n++] = AttribWidth;
        attribs[n++] = attributes.Width;
        attribs[n++] = AttribHeight;
        attribs[n++] = attributes.Height;
        attribs[n++] = AttribFourcc;
        attribs[n++] = unchecked((int)(uint)attributes.Format);
        var withModifier = attributes.Modifier != DrmFormatSet.ModifierInvalid;
        for (var plane = 0; plane < attributes.PlaneCount; plane++)
        {
            attribs[n++] = PlaneFd[plane];
            attribs[n++] = attributes.Fds[plane];
            attribs[n++] = PlaneOffset[plane];
            attribs[n++] = unchecked((int)attributes.Offsets[plane]);
            attribs[n++] = PlanePitch[plane];
            attribs[n++] = unchecked((int)attributes.Strides[plane]);
            if (withModifier)
            {
                attribs[n++] = PlaneModifierLo[plane];
                attribs[n++] = unchecked((int)(attributes.Modifier & 0xFFFFFFFF));
                attribs[n++] = PlaneModifierHi[plane];
                attribs[n++] = unchecked((int)(attributes.Modifier >> 32));
            }
        }

        attribs[n++] = 0x3038;
        nint image;
        fixed (int* attribsPtr = attribs)
        {
            image = _createImage(_display, 0, LinuxDmaBufExt, 0, attribsPtr);
        }

        if (image == 0)
        {
            Log.Debug($"eglCreateImageKHR refused a {attributes.Width}x{attributes.Height} {attributes.Format} dmabuf, modifier 0x{attributes.Modifier:X}");
            return null;
        }

        while (_getError() != GlNoError)
        {
        }

        uint texture;
        _genTextures(1, &texture);
        _bindTexture(GlTexture2D, texture);
        _imageTargetTexture(GlTexture2D, (void*)image);
        var glError = _getError();
        if (glError != GlNoError)
        {
            Log.Debug($"glEGLImageTargetTexture2DOES failed with 0x{glError:X} on a {attributes.Width}x{attributes.Height} {attributes.Format} dmabuf");
            _deleteTextures(1, &texture);
            _destroyImage(_display, image);
            return null;
        }

        return (texture, image);
    }

    public void Destroy(uint texture, nint image)
    {
        _deleteTextures(1, &texture);
        _destroyImage(_display, image);
    }

    private DrmFormatSet QueryFormats()
    {
        var set = new DrmFormatSet();
        var count = 0;
        if (_queryFormats(_display, 0, null, &count) == 0)
        {
            return set;
        }

        var formats = new int[count];
        fixed (int* formatsPtr = formats)
        {
            _queryFormats(_display, count, formatsPtr, &count);
        }

        foreach (var fourcc in formats)
        {
            var format = (DrmFormat)(uint)fourcc;
            if (!PixelFormatInfo.TryGet(format, out _))
            {
                continue;
            }

            var modifierCount = 0;
            _queryModifiers(_display, fourcc, 0, null, null, &modifierCount);
            var modifiers = new ulong[Math.Max(modifierCount, 1)];
            var external = new uint[Math.Max(modifierCount, 1)];
            fixed (ulong* modifiersPtr = modifiers)
            fixed (uint* externalPtr = external)
            {
                _queryModifiers(_display, fourcc, modifierCount, modifiersPtr, externalPtr, &modifierCount);
            }

            for (var i = 0; i < modifierCount; i++)
            {
                if (external[i] == 0)
                {
                    set.Add(format, modifiers[i]);
                }
            }

            set.Add(format, DrmFormatSet.ModifierInvalid);
        }

        return set;
    }

    private string? QueryRenderNode()
    {
        var queryDisplay = TryLoad("eglQueryDisplayAttribEXT");
        var queryDevice = TryLoad("eglQueryDeviceStringEXT");
        if (queryDisplay is null || queryDevice is null)
        {
            return null;
        }

        var queryDisplayAttrib = (delegate* unmanaged[Cdecl]<nint, int, nint*, uint>)queryDisplay;
        var queryDeviceString = (delegate* unmanaged[Cdecl]<nint, int, sbyte*>)queryDevice;
        nint device = 0;
        if (queryDisplayAttrib(_display, EglDeviceExt, &device) == 0 || device == 0)
        {
            return null;
        }

        var node = queryDeviceString(device, DrmRenderNodeFileExt);
        return node is null ? null : new string(node);
    }

    private static void* Load(string name)
    {
        var loaded = TryLoad(name);
        if (loaded is null)
        {
            throw new InvalidOperationException($"{name} unavailable");
        }

        return loaded;
    }

    private static void* TryLoad(string name)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* bytesPtr = bytes)
        {
            return Mesa.Native.Libegl.eglGetProcAddress((sbyte*)bytesPtr);
        }
    }
}
