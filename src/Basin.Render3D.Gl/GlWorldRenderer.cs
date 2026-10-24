using System.Numerics;
using Basin.Diagnostics;
using Basin.Render.Gl;
using Basin.Scene3D;
using Silk.NET.OpenGLES;

namespace Basin.Render3D.Gl;

/// <summary>
/// The GL 3D pass. It draws on the 2D renderer's own <see cref="GlDevice"/> and context, so it samples client textures with no second import. Use it on the compositor thread only.
/// </summary>
public sealed unsafe class GlWorldRenderer : IWorldRenderer
{
    private const int MaxLights = 8;
    private const int MaxJoints = Skeleton.MaxJoints;
    private const int MaxLevels = 8;
    private const GLEnum MaxAnisotropy = (GLEnum)0x84FF;
    private const GLEnum TextureMaxAnisotropy = (GLEnum)0x84FE;
    private const long IdleFrames = 600;
    private const long WarmupFrames = 3;

    private static readonly Matrix4x4 ClipRemap = new(
        1, 0, 0, 0,
        0, -1, 0, 0,
        0, 0, 2, 0,
        0, 0, -1, 1);

    private readonly GlDevice _device;
    private readonly GL _gl;
    private readonly GlWorldProgram _lit;
    private readonly GlWorldProgram _skinned;
    private readonly GlWorldProgram _flat;
    private readonly GlWorldProgram _copy;
    private readonly uint _windowSampler;
    private readonly uint _directSampler;
    private readonly uint _atlasSampler;
    private readonly uint _copyVao;
    private readonly float _deviceAnisotropy;
    private readonly Dictionary<IBuffer, GlWorldTarget> _targets = [];
    private readonly Dictionary<WorldMesh, GlWorldMesh> _meshes = [];
    private readonly Dictionary<SkinnedMesh, GlWorldMesh> _skinnedMeshes = [];
    private readonly Dictionary<WorldImage, uint> _images = [];
    private readonly Dictionary<WorldWindow, GlMipCopy?[]> _copies = [];
    private readonly List<GlDmabufTexture> _sampled = new(32);
    private readonly List<WorldMesh> _evict = [];
    private readonly WorldVisibleSet[] _levels = new WorldVisibleSet[MaxLevels];
    private readonly float[] _lightPositions = new float[MaxLights * 4];
    private readonly float[] _lightColors = new float[MaxLights * 4];
    private readonly float[] _lightDistances = new float[64];
    private readonly int[] _lightOrder = new int[64];
    private readonly float[] _sortKeys = new float[64];
    private readonly int[] _sortItems = new int[64];
    private readonly Action<IBuffer> _forgetTarget;
    private readonly Dictionary<IBuffer, Action> _destroyHandlers = [];
    private int _lightCount;
    private long _frame;
    private float _settingsMax = 1.2f;
    private float _settingsAnisotropy = 8;
    private IBuffer? _failedTarget;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="GlWorldRenderer"/> class. It compiles its programs and creates its samplers on <paramref name="device"/>'s context.
    /// </summary>
    /// <param name="device">The GL device of the 2D renderer. Its context must be current.</param>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is <see langword="null"/>.</exception>
    public GlWorldRenderer(GlDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _gl = device.Gl;
        _lit = new GlWorldProgram(_gl, GlWorldShaders.LitVertex, GlWorldShaders.LitFragment);
        _skinned = new GlWorldProgram(_gl, GlWorldShaders.SkinnedVertex, GlWorldShaders.LitFragment);
        _flat = new GlWorldProgram(_gl, GlWorldShaders.FlatVertex, GlWorldShaders.FlatFragment);
        _copy = new GlWorldProgram(_gl, GlWorldShaders.CopyVertex, GlWorldShaders.CopyFragment);
        _copyVao = _gl.GenVertexArray();
        _deviceAnisotropy = HasExtension("GL_EXT_texture_filter_anisotropic") ? ReadFloat(MaxAnisotropy) : 1;
        _windowSampler = CreateSampler(TextureMinFilter.LinearMipmapLinear, GLEnum.ClampToEdge);
        _directSampler = CreateSampler(TextureMinFilter.Linear, GLEnum.ClampToEdge);
        _atlasSampler = CreateSampler(TextureMinFilter.LinearMipmapLinear, GLEnum.Repeat);
        for (var i = 0; i < _levels.Length; i++)
        {
            _levels[i] = new WorldVisibleSet();
        }

        _forgetTarget = ForgetTarget;
        BasinCounters.Track();
    }

    /// <inheritdoc/>
    public string Name => "gl";

    /// <summary>
    /// Gets the GL device that the pass draws on.
    /// </summary>
    public GlDevice Device => _device;

    /// <summary>
    /// Gets the number of mipmapped window copies that the last frame refreshed.
    /// </summary>
    public int CopiesRefreshed { get; private set; }

    /// <summary>
    /// Gets the number of window layers that the last frame sampled directly, with no mipmapped copy.
    /// </summary>
    public int DirectSamples { get; private set; }

    /// <summary>
    /// Draws the frame into <paramref name="target"/>, with portal levels through the stencil. It restores the GL state it changed and fences the target and the sampled dmabufs.
    /// </summary>
    /// <param name="frame">The world, the view, the visible set and the settings for this output.</param>
    /// <param name="target">The buffer to draw into. The pass caches a render target with a depth and stencil buffer for it until it is destroyed.</param>
    /// <returns><see langword="true"/> when the frame was drawn. <see langword="false"/> when the pass cannot make a complete render target with depth and stencil for <paramref name="target"/>. The pass logs the first failure for a target once.</returns>
    /// <exception cref="ObjectDisposedException">The renderer is disposed.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> is <see langword="null"/>.</exception>
    public bool Render(in WorldFrame frame, IBuffer target)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(target);
        if (_frame < WarmupFrames)
        {
            return RenderCore(frame, target);
        }

        AllocationScope.Begin(region: "GlWorldFrame", forgiving: true);
        try
        {
            return RenderCore(frame, target);
        }
        finally
        {
            AllocationScope.End();
        }
    }

    /// <inheritdoc/>
    public void Forget(WorldWindow window)
    {
        if (_copies.Remove(window, out var copies))
        {
            foreach (var copy in copies)
            {
                copy?.Dispose(_gl);
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var (buffer, target) in _targets)
        {
            buffer.Destroyed -= ForgetDestroyed(buffer);
            target.Dispose(_device);
        }

        _targets.Clear();
        foreach (var mesh in _meshes.Values)
        {
            mesh.Dispose(_gl);
        }

        foreach (var mesh in _skinnedMeshes.Values)
        {
            mesh.Dispose(_gl);
        }

        foreach (var texture in _images.Values)
        {
            _gl.DeleteTexture(texture);
        }

        foreach (var copies in _copies.Values)
        {
            foreach (var copy in copies)
            {
                copy?.Dispose(_gl);
            }
        }

        _meshes.Clear();
        _skinnedMeshes.Clear();
        _images.Clear();
        _copies.Clear();
        _gl.DeleteSampler(_windowSampler);
        _gl.DeleteSampler(_directSampler);
        _gl.DeleteSampler(_atlasSampler);
        _gl.DeleteVertexArray(_copyVao);
        _lit.Dispose(_gl);
        _skinned.Dispose(_gl);
        _flat.Dispose(_gl);
        _copy.Dispose(_gl);
        BasinCounters.Untrack();
    }

    private bool RenderCore(in WorldFrame frame, IBuffer target)
    {
        if (TargetFor(target) is not { } entry)
        {
            return false;
        }

        _frame++;
        _settingsMax = frame.Settings.DirectSampleMax;
        _settingsAnisotropy = frame.Settings.Anisotropy;
        CopiesRefreshed = 0;
        DirectSamples = 0;
        var width = target.Width;
        var height = target.Height;

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, entry.Target.Framebuffer);
        _gl.Viewport(0, 0, (uint)width, (uint)height);
        _gl.Disable(EnableCap.ScissorTest);
        _gl.ColorMask(true, true, true, true);
        _gl.DepthMask(true);
        _gl.StencilMask(0xFF);
        var clear = frame.World.ClearColor;
        _gl.ClearColor(clear.X, clear.Y, clear.Z, 1);
        _gl.ClearDepth(1);
        _gl.ClearStencil(0);
        _gl.Clear((uint)(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit | ClearBufferMask.StencilBufferBit));
        PassState(entry, width, height);

        DrawLevel(frame, entry, frame.View, frame.Visible, 0, width, height);

        RestoreState();
        Publish(entry.Target, target);
        EvictIdle();
        return true;
    }

    private void DrawLevel(in WorldFrame frame, GlWorldTarget entry, in WorldView view, WorldVisibleSet set, int level, int width, int height)
    {
        var world = frame.World;
        var viewProjection = view.ViewProjection * ClipRemap;
        var eye = view.Eye;
        _gl.StencilFunc(StencilFunction.Equal, level, 0xFF);
        _gl.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Keep);
        GatherLights(set, eye, frame.Tick.TargetPresentNanos, frame.Settings.MaxLights);

        UseLit(_lit, world, eye);
        for (var i = 0; i < set.Rooms.Count; i++)
        {
            if (set.Rooms[i].Structure is { } structure)
            {
                DrawMesh(_lit, structure, Matrix4x4.Identity, viewProjection, Vector4.One, lit: true);
            }
        }

        for (var i = 0; i < set.Props.Count; i++)
        {
            var prop = set.Props[i];
            DrawMesh(_lit, prop.Mesh, prop.Model, viewProjection, Vector4.One, lit: true);
        }

        var translucent = 0;
        for (var i = 0; i < set.Windows.Count; i++)
        {
            var window = set.Windows[i];
            if (window.IsTranslucent)
            {
                if (translucent < _sortItems.Length)
                {
                    _sortItems[translucent] = i;
                    _sortKeys[translucent] = -Vector3.DistanceSquared(window.Position, eye);
                    translucent++;
                }

                continue;
            }

            DrawWindow(frame, entry, view, window, viewProjection, eye, width, height);
        }

        for (var r = 0; r < set.Rooms.Count; r++)
        {
            var lights = set.Rooms[r].Lights;
            for (var l = 0; l < lights.Count; l++)
            {
                var light = lights[l];
                var glow = light.Color * MathF.Min(light.IntensityAt(frame.Tick.TargetPresentNanos), 1.5f);
                var model = Matrix4x4.CreateScale(0.06f) * Matrix4x4.CreateTranslation(light.Position);
                SetEmissive(_lit, glow);
                DrawMesh(_lit, WorldPrimitives.Flame, model, viewProjection, new Vector4(0, 0, 0, 1), lit: false);
            }
        }

        SetEmissive(_lit, Vector3.Zero);

        if (level > 0 && world.Avatar is { Visible: true } avatar)
        {
            DrawAvatar(world, avatar, viewProjection, eye);
        }

        if (level + 1 < Math.Min(frame.Settings.PortalDepth + 1, MaxLevels))
        {
            for (var p = 0; p < set.Portals.Count; p++)
            {
                DrawPortal(frame, entry, view, set.Portals[p], level, width, height);
            }

            _gl.StencilFunc(StencilFunction.Equal, level, 0xFF);
            _gl.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Keep);
            GatherLights(set, eye, frame.Tick.TargetPresentNanos, frame.Settings.MaxLights);
        }

        if (translucent > 0)
        {
            Array.Sort(_sortKeys, _sortItems, 0, translucent);
            _gl.DepthMask(false);
            UseLit(_lit, world, eye);
            for (var i = 0; i < translucent; i++)
            {
                DrawWindow(frame, entry, view, set.Windows[_sortItems[i]], viewProjection, eye, width, height);
            }

            _gl.DepthMask(true);
        }
    }

    private void DrawPortal(in WorldFrame frame, GlWorldTarget entry, in WorldView view, WorldPortal portal, int level, int width, int height)
    {
        if (portal.Target is not { } target || Vector3.Dot(view.Eye - portal.Center, portal.Normal) <= 0.01f)
        {
            return;
        }

        var viewProjection = view.ViewProjection * ClipRemap;
        var model = Matrix4x4.CreateScale(portal.Width, portal.Height, 1) * portal.Frame;
        var mvp = model * viewProjection;
        _gl.UseProgram(_flat.Program);

        _gl.ColorMask(false, false, false, false);
        _gl.DepthMask(false);
        _gl.StencilFunc(StencilFunction.Equal, level, 0xFF);
        _gl.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Incr);
        DrawFlat(WorldPrimitives.Quad, mvp, far: false, Vector4.Zero);

        var clear = frame.World.ClearColor;
        _gl.ColorMask(true, true, true, true);
        _gl.DepthMask(true);
        _gl.DepthFunc(DepthFunction.Always);
        _gl.StencilFunc(StencilFunction.Equal, level + 1, 0xFF);
        _gl.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Keep);
        _gl.Disable(EnableCap.Blend);
        DrawFlat(WorldPrimitives.Quad, mvp, far: true, new Vector4(clear, 1));
        _gl.Enable(EnableCap.Blend);
        _gl.DepthFunc(DepthFunction.Less);

        var inner = view.Through(portal.TransferTo(target), target.Plane);
        var set = _levels[level + 1];
        set.Clear();
        WorldVisibility.Collect(frame.World, inner, frame.Settings.RoomDepth, set);
        for (var i = 0; i < set.Surfaces.Count; i++)
        {
            if (!frame.Visible.ContainsSurface(set.Surfaces[i]))
            {
                frame.Visible.Surfaces.Add(set.Surfaces[i]);
            }
        }

        DrawLevel(frame, entry, inner, set, level + 1, width, height);

        _gl.UseProgram(_flat.Program);
        _gl.ColorMask(false, false, false, false);
        _gl.DepthFunc(DepthFunction.Always);
        _gl.StencilFunc(StencilFunction.Equal, level + 1, 0xFF);
        _gl.StencilOp(StencilOp.Keep, StencilOp.Keep, StencilOp.Decr);
        DrawFlat(WorldPrimitives.Quad, mvp, far: false, Vector4.Zero);
        _gl.DepthFunc(DepthFunction.Less);
        _gl.ColorMask(true, true, true, true);
    }

    private void DrawWindow(
        in WorldFrame frame, GlWorldTarget entry, in WorldView view, WorldWindow window,
        in Matrix4x4 viewProjection, Vector3 eye, int width, int height)
    {
        var model = window.Model;
        var slab = Matrix4x4.CreateScale(window.Width, window.Height, window.Depth) * model;
        var edge = window.Focused ? window.FocusColor : window.EdgeColor;
        if (window.Flash > 0)
        {
            edge = Vector4.Lerp(edge, new Vector4(1, 1, 1, 1), Math.Clamp(window.Flash, 0, 1));
        }

        var opacity = window.Opacity;
        _gl.UseProgram(_lit.Program);
        if (window.ShownFrame is { } shown)
        {
            DrawFrame(window, shown, viewProjection);
        }
        else
        {
            if (window.Focused)
            {
                SetEmissive(_lit, new Vector3(edge.X, edge.Y, edge.Z) * 0.35f);
            }

            DrawMesh(_lit, WorldPrimitives.SlabEdges, slab, viewProjection, edge with { W = opacity }, lit: true);
            SetEmissive(_lit, Vector3.Zero);
            DrawMesh(_lit, WorldPrimitives.SlabBack, slab, viewProjection, new Vector4(0.08f, 0.08f, 0.09f, opacity), lit: true);
        }

        var layers = window.Layers;
        var metersX = window.MetersPerPixel;
        var metersY = window.SurfaceHeight > 0 ? window.Height / window.SurfaceHeight : 0;
        for (var l = 0; l < layers.Length; l++)
        {
            var layer = layers[l];
            if (layer.Texture is not { } texture || layer.Width <= 0 || layer.Height <= 0)
            {
                continue;
            }

            var centerX = ((layer.X + (layer.Width * 0.5f)) * metersX) - (window.Width * 0.5f);
            var centerY = (window.Height * 0.5f) - ((layer.Y + (layer.Height * 0.5f)) * metersY);
            var quad = Matrix4x4.CreateScale(layer.Width * metersX, layer.Height * metersY, 1) *
                Matrix4x4.CreateTranslation(centerX, centerY, window.LayerDepth(layer) + 0.0001f) * model;
            if (!BindWindowTexture(entry, view, window, l, layer, texture, quad, width, height, out var uvRect, out var forceOpaque))
            {
                continue;
            }

            _gl.UseProgram(_lit.Program);
            _gl.Uniform4(_lit.UvRect, uvRect.X, uvRect.Y, uvRect.Z, uvRect.W);
            _gl.Uniform1(_lit.ForceOpaque, forceOpaque ? 1f : 0f);
            DrawMesh(_lit, WorldPrimitives.Quad, quad, viewProjection, new Vector4(1, 1, 1, opacity * window.CanvasOpacity), lit: !window.Focused, textured: true, bindImage: false);
            _gl.Uniform4(_lit.UvRect, 0f, 0f, 1f, 1f);
            _gl.Uniform1(_lit.ForceOpaque, 0f);
        }
    }

    private void DrawFrame(WorldWindow window, WorldWindowFrame frame, in Matrix4x4 viewProjection)
    {
        WorldFrameShading.Frame(window, frame, out var albedo, out var emissive);
        var material = frame.Material;
        if (frame.Moulding is { } moulding)
        {
            SetEmissive(_lit, emissive);
            SetSpecular(_lit, material.SpecularStrength, material.Shininess);
            DrawMesh(_lit, moulding, window.Model, viewProjection, albedo, lit: true);
            SetEmissive(_lit, Vector3.Zero);
        }

        SetSpecular(_lit, 0, 1);
        DrawMesh(_lit, WorldPrimitives.SlabBack, WorldFrameShading.Back(window, frame), viewProjection,
            WorldFrameShading.BackColor with { W = window.Opacity }, lit: true);
        if (!frame.HasPlaque)
        {
            SetSpecular(_lit, 0.25f, 24);
            return;
        }

        var plaque = frame.PlaqueMaterial;
        var tint = plaque.Albedo with { W = plaque.Albedo.W * window.Opacity };
        SetSpecular(_lit, plaque.SpecularStrength, plaque.Shininess);
        var body = WorldFrameShading.PlaqueBody(window, frame);
        DrawMesh(_lit, WorldPrimitives.SlabEdges, body, viewProjection, tint, lit: true);
        var face = WorldFrameShading.PlaqueFace(window, frame);
        if (frame.PlaqueTexture is { } texture && GlTextures.TryOpen(texture, out var opened) && opened.Width > 0 && opened.Height > 0)
        {
            if (texture is GlDmabufTexture dmabuf && !_sampled.Contains(dmabuf))
            {
                _sampled.Add(dmabuf);
            }

            var source = frame.PlaqueSource.Width > 0 && frame.PlaqueSource.Height > 0
                ? frame.PlaqueSource
                : new FBox(0, 0, opened.Width, opened.Height);
            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.BindTexture(TextureTarget.Texture2D, opened.Name);
            _gl.BindSampler(0, _directSampler);
            _gl.Uniform4(_lit.UvRect, (float)(source.X / opened.Width), (float)(source.Y / opened.Height),
                (float)(source.Width / opened.Width), (float)(source.Height / opened.Height));
            _gl.Uniform1(_lit.ForceOpaque, opened.HasAlpha ? 0f : 1f);
            DrawMesh(_lit, WorldPrimitives.Quad, face, viewProjection, new Vector4(1, 1, 1, window.Opacity), lit: true, textured: true, bindImage: false);
            _gl.Uniform4(_lit.UvRect, 0f, 0f, 1f, 1f);
            _gl.Uniform1(_lit.ForceOpaque, 0f);
        }
        else
        {
            DrawMesh(_lit, WorldPrimitives.Quad, face, viewProjection, tint, lit: true, textured: false, bindImage: false);
        }

        SetSpecular(_lit, 0.25f, 24);
    }

    private bool BindWindowTexture(
        GlWorldTarget entry, in WorldView view, WorldWindow window, int index, in WorldSurfaceLayer layer,
        ITexture texture, in Matrix4x4 quad, int width, int height, out Vector4 uvRect, out bool forceOpaque)
    {
        uvRect = default;
        forceOpaque = false;
        if (!GlTextures.TryOpen(texture, out var opened))
        {
            return false;
        }

        if (texture is GlDmabufTexture dmabuf && !_sampled.Contains(dmabuf))
        {
            _sampled.Add(dmabuf);
        }

        forceOpaque = !opened.HasAlpha || layer.Opaque;
        var source = layer.Source.Width > 0 && layer.Source.Height > 0
            ? layer.Source
            : new FBox(0, 0, opened.Width, opened.Height);
        uvRect = new Vector4(
            (float)(source.X / opened.Width), (float)(source.Y / opened.Height),
            (float)(source.Width / opened.Width), (float)(source.Height / opened.Height));

        var ratio = TexelRatio(view, quad, source, width, height);
        _gl.ActiveTexture(TextureUnit.Texture0);
        if (ratio <= _settingsMax)
        {
            _gl.BindTexture(TextureTarget.Texture2D, opened.Name);
            _gl.BindSampler(0, _directSampler);
            DirectSamples++;
            return true;
        }

        var copy = EnsureCopy(entry, window, index, layer, texture, opened, width, height);
        _gl.BindTexture(TextureTarget.Texture2D, copy.Texture);
        _gl.BindSampler(0, _windowSampler);
        _gl.SamplerParameter(_windowSampler, TextureMaxAnisotropy, Math.Clamp(_settingsAnisotropy, 1, _deviceAnisotropy));
        return true;
    }

    private GlMipCopy EnsureCopy(
        GlWorldTarget entry, WorldWindow window, int index, in WorldSurfaceLayer layer, ITexture texture,
        in GlTextureView opened, int width, int height)
    {
        if (!_copies.TryGetValue(window, out var copies) || copies.Length <= index)
        {
            AllocationScope.Pause();
            var grown = new GlMipCopy?[Math.Max(4, index + 1)];
            copies?.CopyTo(grown, 0);
            copies = grown;
            _copies[window] = copies;
            AllocationScope.Resume();
        }

        var copy = copies[index];
        if (copy is not null && (copy.Width != opened.Width || copy.Height != opened.Height))
        {
            copy.Dispose(_gl);
            copy = null;
        }

        if (copy is null)
        {
            AllocationScope.Pause();
            copy = GlMipCopy.Create(_gl, opened.Width, opened.Height);
            copies[index] = copy;
            AllocationScope.Resume();
        }

        if (!ReferenceEquals(copy.Source, texture) || copy.Version != layer.ContentVersion)
        {
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, copy.Framebuffer);
            _gl.Viewport(0, 0, (uint)copy.Width, (uint)copy.Height);
            _gl.Disable(EnableCap.DepthTest);
            _gl.Disable(EnableCap.StencilTest);
            _gl.Disable(EnableCap.CullFace);
            _gl.Disable(EnableCap.Blend);
            _gl.UseProgram(_copy.Program);
            _gl.ActiveTexture(TextureUnit.Texture0);
            _gl.BindTexture(TextureTarget.Texture2D, opened.Name);
            _gl.BindSampler(0, _directSampler);
            _gl.Uniform1(_copy.Texture, 0);
            _gl.Uniform4(_copy.UvRect, 0f, 0f, 1f, 1f);
            _gl.Uniform1(_copy.ForceOpaque, opened.HasAlpha ? 0f : 1f);
            _gl.BindVertexArray(_copyVao);
            _gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
            _gl.BindTexture(TextureTarget.Texture2D, copy.Texture);
            _gl.GenerateMipmap(TextureTarget.Texture2D);
            copy.Source = texture;
            copy.Version = layer.ContentVersion;
            CopiesRefreshed++;
            PassState(entry, width, height);
        }

        return copy;
    }

    private static float TexelRatio(in WorldView view, in Matrix4x4 quad, in FBox source, int width, int height)
    {
        var clip = quad * view.ViewProjection;
        var a = Screen(clip, new Vector3(-0.5f, 0.5f, 0), width, height);
        var b = Screen(clip, new Vector3(0.5f, 0.5f, 0), width, height);
        var c = Screen(clip, new Vector3(-0.5f, -0.5f, 0), width, height);
        if (a.W <= 0 || b.W <= 0 || c.W <= 0)
        {
            return float.MaxValue;
        }

        var across = Vector2.Distance(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y));
        var down = Vector2.Distance(new Vector2(a.X, a.Y), new Vector2(c.X, c.Y));
        var ratioX = across > 0.5f ? (float)source.Width / across : float.MaxValue;
        var ratioY = down > 0.5f ? (float)source.Height / down : float.MaxValue;
        return MathF.Max(ratioX, ratioY);

        static Vector4 Screen(in Matrix4x4 clip, Vector3 local, int width, int height)
        {
            var p = Vector4.Transform(new Vector4(local, 1), clip);
            if (p.W <= 1e-6f)
            {
                return new Vector4(0, 0, 0, -1);
            }

            return new Vector4((p.X / p.W) * width * 0.5f, (p.Y / p.W) * height * 0.5f, 0, 1);
        }
    }

    private void DrawAvatar(World world, WorldAvatar avatar, in Matrix4x4 viewProjection, Vector3 eye)
    {
        var model = avatar.Model;
        _gl.Disable(EnableCap.CullFace);
        if (!_skinnedMeshes.TryGetValue(avatar.Mesh, out var mesh))
        {
            AllocationScope.Pause();
            mesh = GlWorldMesh.Create(_gl, avatar.Mesh);
            _skinnedMeshes[avatar.Mesh] = mesh;
            AllocationScope.Resume();
        }

        UseLit(_skinned, world, eye);
        var palette = avatar.Pose.Palette;
        var joints = Math.Min(palette.Length, MaxJoints);
        fixed (Matrix4x4* data = palette)
        {
            _gl.UniformMatrix4(_skinned.Joints, (uint)joints, false, (float*)data);
        }

        BindImage(avatar.Mesh.Image);
        SetMatrices(_skinned, model, viewProjection);
        SetMaterial(_skinned, Vector4.One, lit: true, textured: avatar.Mesh.Image is not null);
        mesh.Draw(_gl);

        UseLit(_lit, world, eye);
        var attachments = avatar.Attachments;
        for (var a = 0; a < attachments.Count; a++)
        {
            var attachment = attachments[a];
            if (attachment.Visible)
            {
                DrawMesh(_lit, attachment.Mesh, avatar.Pose.World[attachment.Node] * model, viewProjection, Vector4.One, lit: true);
            }
        }

        _gl.Enable(EnableCap.CullFace);
    }

    private void DrawMesh(
        GlWorldProgram program, WorldMesh mesh, in Matrix4x4 model, in Matrix4x4 viewProjection, Vector4 tint,
        bool lit, bool textured = true, bool bindImage = true)
    {
        var gpu = MeshFor(mesh);
        var hasTexture = textured && (!bindImage || mesh.Image is not null);
        if (bindImage && mesh.Image is not null)
        {
            BindImage(mesh.Image);
        }

        SetMatrices(program, model, viewProjection);
        SetMaterial(program, tint, lit, hasTexture);
        gpu.Draw(_gl);
    }

    private void DrawFlat(WorldMesh mesh, in Matrix4x4 mvp, bool far, Vector4 color)
    {
        var gpu = MeshFor(mesh);
        var local = mvp;
        _gl.UniformMatrix4(_flat.Mvp, 1, false, (float*)&local);
        _gl.Uniform1(_flat.Far, far ? 1f : 0f);
        _gl.Uniform4(_flat.Color, color.X, color.Y, color.Z, color.W);
        gpu.Draw(_gl);
    }

    private GlWorldMesh MeshFor(WorldMesh mesh)
    {
        if (!_meshes.TryGetValue(mesh, out var gpu))
        {
            AllocationScope.Pause();
            gpu = GlWorldMesh.Create(_gl, mesh);
            _meshes[mesh] = gpu;
            AllocationScope.Resume();
        }

        gpu.LastUsed = _frame;
        return gpu;
    }

    private void BindImage(WorldImage? image)
    {
        _gl.ActiveTexture(TextureUnit.Texture0);
        if (image is null)
        {
            return;
        }

        if (!_images.TryGetValue(image, out var texture))
        {
            AllocationScope.Pause();
            texture = UploadImage(image);
            _images[image] = texture;
            AllocationScope.Resume();
        }

        _gl.BindTexture(TextureTarget.Texture2D, texture);
        _gl.BindSampler(0, _atlasSampler);
    }

    private uint UploadImage(WorldImage image)
    {
        var levels = 1 + (int)Math.Floor(Math.Log2(Math.Max(image.Width, image.Height)));
        var texture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, texture);
        _gl.TexStorage2D(TextureTarget.Texture2D, (uint)levels, SizedInternalFormat.Rgba8, (uint)image.Width, (uint)image.Height);
        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        fixed (byte* data = image.Rgba)
        {
            _gl.TexSubImage2D(TextureTarget.Texture2D, 0, 0, 0, (uint)image.Width, (uint)image.Height, PixelFormat.Rgba, PixelType.UnsignedByte, data);
        }

        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 4);
        _gl.GenerateMipmap(TextureTarget.Texture2D);
        _gl.SamplerParameter(_atlasSampler, TextureMaxAnisotropy, Math.Clamp(_settingsAnisotropy, 1, _deviceAnisotropy));
        return texture;
    }

    private void UseLit(GlWorldProgram program, World world, Vector3 eye)
    {
        _gl.UseProgram(program.Program);
        _gl.Uniform1(program.Texture, 0);
        _gl.Uniform3(program.Ambient, world.Ambient.X, world.Ambient.Y, world.Ambient.Z);
        _gl.Uniform3(program.Eye, eye.X, eye.Y, eye.Z);
        _gl.Uniform3(program.Emissive, 0f, 0f, 0f);
        _gl.Uniform2(program.Specular, 0.25f, 24f);
        _gl.Uniform4(program.UvRect, 0f, 0f, 1f, 1f);
        _gl.Uniform1(program.ForceOpaque, 0f);
        _gl.Uniform1(program.LightCount, _lightCount);
        fixed (float* positions = _lightPositions)
        fixed (float* colors = _lightColors)
        {
            _gl.Uniform4(program.LightPosition, MaxLights, positions);
            _gl.Uniform4(program.LightColor, MaxLights, colors);
        }
    }

    private void SetEmissive(GlWorldProgram program, Vector3 emissive) =>
        _gl.Uniform3(program.Emissive, emissive.X, emissive.Y, emissive.Z);

    private void SetSpecular(GlWorldProgram program, float strength, float shininess) =>
        _gl.Uniform2(program.Specular, strength, shininess);

    private void SetMatrices(GlWorldProgram program, in Matrix4x4 model, in Matrix4x4 viewProjection)
    {
        var mvp = model * viewProjection;
        var local = model;
        _gl.UniformMatrix4(program.Mvp, 1, false, (float*)&mvp);
        _gl.UniformMatrix4(program.Model, 1, false, (float*)&local);
    }

    private void SetMaterial(GlWorldProgram program, Vector4 tint, bool lit, bool textured)
    {
        _gl.Uniform4(program.Tint, tint.X, tint.Y, tint.Z, tint.W);
        _gl.Uniform1(program.Lit, lit ? 1f : 0f);
        _gl.Uniform1(program.Textured, textured ? 1f : 0f);
    }

    private void GatherLights(WorldVisibleSet set, Vector3 eye, long nanos, int maxLights)
    {
        var count = 0;
        for (var r = 0; r < set.Rooms.Count; r++)
        {
            var lights = set.Rooms[r].Lights;
            for (var l = 0; l < lights.Count && count < _lightOrder.Length; l++)
            {
                _lightDistances[count] = Vector3.DistanceSquared(lights[l].Position, eye);
                _lightOrder[count] = (r << 16) | l;
                count++;
            }
        }

        Array.Sort(_lightDistances, _lightOrder, 0, count);
        _lightCount = Math.Min(count, Math.Clamp(maxLights, 0, MaxLights));
        for (var i = 0; i < _lightCount; i++)
        {
            var light = set.Rooms[_lightOrder[i] >> 16].Lights[_lightOrder[i] & 0xFFFF];
            var intensity = light.IntensityAt(nanos);
            _lightPositions[(i * 4) + 0] = light.Position.X;
            _lightPositions[(i * 4) + 1] = light.Position.Y;
            _lightPositions[(i * 4) + 2] = light.Position.Z;
            _lightPositions[(i * 4) + 3] = MathF.Max(light.Range, 0.01f);
            _lightColors[(i * 4) + 0] = light.Color.X * intensity;
            _lightColors[(i * 4) + 1] = light.Color.Y * intensity;
            _lightColors[(i * 4) + 2] = light.Color.Z * intensity;
            _lightColors[(i * 4) + 3] = 1;
        }
    }

    private void PassState(GlWorldTarget entry, int width, int height)
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, entry.Target.Framebuffer);
        _gl.Viewport(0, 0, (uint)width, (uint)height);
        _gl.Enable(EnableCap.DepthTest);
        _gl.DepthFunc(DepthFunction.Less);
        _gl.Enable(EnableCap.StencilTest);
        _gl.Enable(EnableCap.CullFace);
        _gl.CullFace(TriangleFace.Back);
        _gl.FrontFace(FrontFaceDirection.CW);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendFunc(BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha);
    }

    private void RestoreState()
    {
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.StencilTest);
        _gl.Disable(EnableCap.CullFace);
        _gl.FrontFace(FrontFaceDirection.Ccw);
        _gl.DepthMask(true);
        _gl.ColorMask(true, true, true, true);
        _gl.StencilMask(0xFF);
        _gl.BindSampler(0, 0);
        _gl.BindVertexArray(0);
        _gl.UseProgram(0);
        _gl.ActiveTexture(TextureUnit.Texture0);
    }

    private void Publish(GlRenderTarget entry, IBuffer target)
    {
        if (entry.IsCpuReadback)
        {
            _sampled.Clear();
            entry.ReadInto(_device, target);
            return;
        }

        var fence = _device.ExportFence();
        if (fence < 0)
        {
            _gl.Flush();
            _sampled.Clear();
            return;
        }

        RenderFences.PublishFenceTo(entry.Attributes, forWrite: true, fence);
        for (var i = 0; i < _sampled.Count; i++)
        {
            RenderFences.PublishFenceTo(_sampled[i].Attributes, forWrite: false, fence);
        }

        _sampled.Clear();
        RenderFences.CloseFence(fence);
    }

    private GlWorldTarget? TargetFor(IBuffer target)
    {
        if (_targets.TryGetValue(target, out var entry))
        {
            return entry;
        }

        AllocationScope.Pause();
        try
        {
            if (GlWorldTarget.TryCreate(_device, target, out var error) is not { } created)
            {
                if (!ReferenceEquals(_failedTarget, target))
                {
                    _failedTarget = target;
                    GlWorldLog.Log.Warn(error!, $"world frame not drawn");
                }

                return null;
            }

            entry = created;
            _targets[target] = entry;
            target.Destroyed += ForgetDestroyed(target);
            return entry;
        }
        finally
        {
            AllocationScope.Resume();
        }
    }

    private Action ForgetDestroyed(IBuffer buffer)
    {
        if (!_destroyHandlers.TryGetValue(buffer, out var handler))
        {
            handler = () => _forgetTarget(buffer);
            _destroyHandlers[buffer] = handler;
        }

        return handler;
    }

    private void ForgetTarget(IBuffer buffer)
    {
        _destroyHandlers.Remove(buffer);
        if (_targets.Remove(buffer, out var entry) && !_disposed)
        {
            entry.Dispose(_device);
        }
    }

    private void EvictIdle()
    {
        if (_frame % 120 != 0)
        {
            return;
        }

        foreach (var (mesh, gpu) in _meshes)
        {
            if (_frame - gpu.LastUsed > IdleFrames && !IsPrimitive(mesh))
            {
                _evict.Add(mesh);
            }
        }

        foreach (var mesh in _evict)
        {
            _meshes.Remove(mesh, out var gpu);
            gpu?.Dispose(_gl);
        }

        _evict.Clear();
    }

    private static bool IsPrimitive(WorldMesh mesh) =>
        ReferenceEquals(mesh, WorldPrimitives.Quad) || ReferenceEquals(mesh, WorldPrimitives.SlabEdges) ||
        ReferenceEquals(mesh, WorldPrimitives.SlabBack) || ReferenceEquals(mesh, WorldPrimitives.Flame);

    private uint CreateSampler(TextureMinFilter min, GLEnum wrap)
    {
        var sampler = _gl.GenSampler();
        _gl.SamplerParameter(sampler, SamplerParameterI.MinFilter, (int)min);
        _gl.SamplerParameter(sampler, SamplerParameterI.MagFilter, (int)TextureMagFilter.Linear);
        _gl.SamplerParameter(sampler, SamplerParameterI.WrapS, (int)wrap);
        _gl.SamplerParameter(sampler, SamplerParameterI.WrapT, (int)wrap);
        return sampler;
    }

    private bool HasExtension(string name)
    {
        _gl.GetInteger(GetPName.NumExtensions, out var count);
        for (var i = 0u; i < (uint)count; i++)
        {
            if (_gl.GetStringS(StringName.Extensions, i) == name)
            {
                return true;
            }
        }

        return false;
    }

    private float ReadFloat(GLEnum name)
    {
        float value = 1;
        _gl.GetFloat(name, &value);
        return value;
    }
}
