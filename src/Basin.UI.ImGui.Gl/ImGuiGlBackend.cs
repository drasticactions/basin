using Basin.Capabilities;
using Basin.Render.Gl;
using Silk.NET.OpenGLES;

namespace Basin.UI.ImGui;

internal sealed unsafe class ImGuiGlBackend : IImGuiBackend
{
    private readonly List<uint> _textures = [];
    private readonly List<ImGuiGlTarget> _targets = [];
    private readonly ulong[] _modifiers;
    private bool _ownsDevice;
    private readonly bool _ownsAllocator;
    private bool _syncReported;
    private bool _disposed;

    internal ImGuiGlBackend(GlDevice device, IAllocator allocator, ulong[] modifiers, bool separateContext, bool ownsAllocator)
    {
        Device = device;
        Allocator = allocator;
        _modifiers = modifiers;
        _ownsAllocator = ownsAllocator;
        Context = new ImGuiGlContext(device, switches: separateContext);
        using var current = Context.Enter();
        Program = new ImGuiGlProgram(device.Gl);
    }

    public string Name => "gl";

    public UITargetKind Produces => UITargetKind.Dmabuf;

    public bool HasVertexOffset => Program.HasBaseVertex;

    public int FramesInFlight => 2;

    internal GlDevice Device { get; }

    internal IAllocator Allocator { get; }

    internal ImGuiGlContext Context { get; }

    internal ImGuiGlProgram Program { get; }

    internal bool OwnsDevice => _ownsDevice;

    internal ReadOnlySpan<ulong> Modifiers => _modifiers;

    internal int FencedFrames { get; private set; }

    internal int FinishedFrames { get; private set; }

    internal int LiveTextures
    {
        get
        {
            var live = 0;
            foreach (var name in _textures)
            {
                live += name == 0 ? 0 : 1;
            }

            return live;
        }
    }

    public IImGuiTarget? CreateTarget()
    {
        if (_disposed)
        {
            return null;
        }

        var target = new ImGuiGlTarget(this);
        _targets.Add(target);
        return target;
    }

    public int CreateTexture(int width, int height)
    {
        if (_disposed)
        {
            return -1;
        }

        using var current = Context.Enter();
        var gl = Device.Gl;
        var name = gl.GenTexture();
        gl.BindTexture(TextureTarget.Texture2D, name);
        gl.TexImage2D(
            TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)Math.Max(1, width), (uint)Math.Max(1, height), 0,
            PixelFormat.Rgba, PixelType.UnsignedByte, null);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        gl.BindTexture(TextureTarget.Texture2D, 0);

        var slot = _textures.IndexOf(0);
        if (slot < 0)
        {
            _textures.Add(name);
            return _textures.Count - 1;
        }

        _textures[slot] = name;
        return slot;
    }

    public void UpdateTexture(int texture, int x, int y, int width, int height, byte* pixels, int pitch)
    {
        var name = NameOf(texture);
        if (name == 0 || width <= 0 || height <= 0)
        {
            return;
        }

        using var current = Context.Enter();
        var gl = Device.Gl;
        gl.BindTexture(TextureTarget.Texture2D, name);
        gl.PixelStore(PixelStoreParameter.UnpackRowLength, pitch / 4);
        gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        gl.TexSubImage2D(
            TextureTarget.Texture2D, 0, x, y, (uint)width, (uint)height, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);
        gl.PixelStore(PixelStoreParameter.UnpackRowLength, 0);
        gl.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
        gl.BindTexture(TextureTarget.Texture2D, 0);
    }

    public void DestroyTexture(int texture)
    {
        var name = NameOf(texture);
        if (name == 0)
        {
            return;
        }

        using var current = Context.Enter();
        Device.Gl.DeleteTexture(name);
        _textures[texture] = 0;
    }

    internal uint NameOf(int texture) => (uint)texture < (uint)_textures.Count ? _textures[texture] : 0;

    internal void TakeDevice(bool owns) => _ownsDevice = owns;

    internal void Forget(ImGuiGlTarget target) => _targets.Remove(target);

    internal void ReportSync(bool fenced)
    {
        if (fenced)
        {
            FencedFrames++;
        }
        else
        {
            FinishedFrames++;
        }

        if (_syncReported)
        {
            return;
        }

        _syncReported = true;
        if (fenced)
        {
            ImGuiGlLog.Log.Debug($"chrome on {Device.DevicePath} publishes a native fence to each dmabuf it draws");
        }
        else
        {
            ImGuiGlLog.Log.Debug($"chrome on {Device.DevicePath} exports no native fence and waits on glFinish for each frame");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var target in _targets.ToArray())
        {
            target.Dispose();
        }

        _targets.Clear();
        using (Context.Enter())
        {
            foreach (var name in _textures)
            {
                if (name != 0)
                {
                    Device.Gl.DeleteTexture(name);
                }
            }

            _textures.Clear();
            Program.Dispose();
        }

        if (_ownsAllocator)
        {
            Allocator.Dispose();
        }

        if (_ownsDevice)
        {
            var saved = ImGuiGlContext.Save();
            Device.Dispose();
            saved.Dispose();
        }
    }
}
