using Basin.Scene;
using Pixman;
using Xunit;

namespace Basin.Tests;

public sealed class SceneOutputLifetimeTests
{
    private sealed class CountingTexture(int width, int height) : ITexture
    {
        public int Disposals { get; private set; }

        public int Width => width;

        public int Height => height;

        public void Dispose() => Disposals++;
    }

    private sealed class CountingRenderer : IRenderer
    {
        private readonly List<CountingTexture> _textures = [];

        public int TextureDisposals
        {
            get
            {
                var total = 0;
                foreach (var texture in _textures)
                {
                    total += texture.Disposals;
                }

                return total;
            }
        }

        public ITexture? ImportTexture(IBuffer buffer)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            var texture = new CountingTexture(buffer.Width, buffer.Height);
            _textures.Add(texture);
            return texture;
        }

        public IRenderPass BeginBufferPass(IBuffer target, in RenderPassOptions options) => new CountingPass();

        public void Dispose()
        {
        }
    }

    private sealed class CountingPass : IRenderPass
    {
        public void AddRect(in RenderColor color, in Box box, PixmanRegion32? clip = null)
        {
        }

        public void AddTexture(ITexture texture, in TextureRenderOptions options)
        {
        }

        public void AddMesh(ITexture? texture, ReadOnlySpan<MeshVertex> vertices, in MeshRenderOptions options)
        {
        }

        public bool Submit() => true;
    }

    [Fact]
    public void An_output_destroyed_after_its_scene_output_does_not_dispose_it_twice()
    {
        using var host = new CompositorTestHost();
        var renderer = new CountingRenderer();
        var sceneOutput = new SceneOutput(host.Scene, host.Output);
        using var swapchain = new Swapchain(
            new ShmAllocator(), 160, 120, DrmFormat.Xrgb8888, [DrmFormatSet.ModifierLinear]);
        using var state = new OutputState();
        var options = new SceneCommitOptions { AllowDirectScanout = false };

        _ = new SceneRect(host.Scene.Root, 160, 120, new RenderColor(0f, 0f, 0.5f, 1f));
        var cursorImage = new MemoryBuffer(8, 8, DrmFormat.Argb8888);
        sceneOutput.SetSoftwareCursor(cursorImage, 0, 0);
        sceneOutput.MoveSoftwareCursor(100, 60);
        Assert.True(sceneOutput.Commit(renderer, swapchain, state, options));
        Assert.Equal(0, renderer.TextureDisposals);

        sceneOutput.Dispose();
        Assert.Equal(1, renderer.TextureDisposals);

        host.Output.Destroy();
        Assert.Equal(1, renderer.TextureDisposals);

        cursorImage.Destroy();
    }
}
