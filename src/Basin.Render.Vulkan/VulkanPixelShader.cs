using Basin.Diagnostics;
using Silk.NET.Vulkan;

namespace Basin.Render.Vulkan;

internal sealed unsafe class VulkanPixelShader : IPixelShader
{
    private readonly VulkanRenderer _renderer;
    private readonly ShaderModule _module;
    private readonly PixelShaderUniformValue[] _values;
    private readonly PixelShaderUniformType[] _types;
    private readonly int[] _offsets;
    private readonly Pipeline[] _pipelines = new Pipeline[4];
    private bool _disposed;

    internal readonly bool SamplesTexture;

    internal VulkanPixelShader(VulkanRenderer renderer, ShaderModule module, ReadOnlySpan<PixelShaderUniform> uniforms, bool samplesTexture)
    {
        _renderer = renderer;
        _module = module;
        SamplesTexture = samplesTexture;
        _values = new PixelShaderUniformValue[uniforms.Length];
        _types = new PixelShaderUniformType[uniforms.Length];
        _offsets = new int[uniforms.Length];
        var cursor = 12;
        for (var i = 0; i < uniforms.Length; i++)
        {
            _types[i] = uniforms[i].Type;
            var (align, size) = uniforms[i].Type switch
            {
                PixelShaderUniformType.Float => (4, 4),
                PixelShaderUniformType.Float2 => (8, 8),
                PixelShaderUniformType.Float3 => (16, 12),
                _ => (16, 16),
            };
            cursor = (cursor + align - 1) / align * align;
            _offsets[i] = cursor;
            cursor += size;
        }

        if (cursor > (int)VulkanRenderer.ShaderBlockCapacity)
        {
            renderer.Dev.Api.DestroyShaderModule(renderer.Dev.Device, module, null);
            throw new ArgumentException("the declared uniforms exceed the uniform block capacity", nameof(uniforms));
        }

        BasinCounters.Track();
    }

    public void SetUniforms(ReadOnlySpan<PixelShaderUniformValue> values)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (values.Length != _values.Length)
        {
            throw new ArgumentException("value count must match the declared uniform count", nameof(values));
        }

        values.CopyTo(_values);
    }

    internal void WriteBlock(void* destination, float width, float height, float alpha)
    {
        var floats = (float*)destination;
        floats[0] = width;
        floats[1] = height;
        floats[2] = alpha;
        for (var i = 0; i < _values.Length; i++)
        {
            var value = _values[i];
            var at = floats + (_offsets[i] / 4);
            switch (_types[i])
            {
                case PixelShaderUniformType.Float:
                    at[0] = value.X;
                    break;
                case PixelShaderUniformType.Float2:
                    at[0] = value.X;
                    at[1] = value.Y;
                    break;
                case PixelShaderUniformType.Float3:
                    at[0] = value.X;
                    at[1] = value.Y;
                    at[2] = value.Z;
                    break;
                case PixelShaderUniformType.Float4:
                    at[0] = value.X;
                    at[1] = value.Y;
                    at[2] = value.Z;
                    at[3] = value.W;
                    break;
            }
        }
    }

    internal Pipeline PipelineFor(bool twoPass, bool srgbDecode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var index = (twoPass ? 2 : 0) | (srgbDecode ? 1 : 0);
        if (_pipelines[index].Handle == 0)
        {
            _pipelines[index] = _renderer.CreateConsumerPipeline(_module, twoPass, SamplesTexture, SamplesTexture ? (srgbDecode ? 1 : 0) : null);
        }

        return _pipelines[index];
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var vk = _renderer.Dev.Api;
        var device = _renderer.Dev.Device;
        for (var i = 0; i < _pipelines.Length; i++)
        {
            if (_pipelines[i].Handle != 0)
            {
                vk.DestroyPipeline(device, _pipelines[i], null);
                _pipelines[i] = default;
            }
        }

        vk.DestroyShaderModule(device, _module, null);
        BasinCounters.Untrack();
    }
}
