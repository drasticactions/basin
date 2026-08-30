using Basin;
using Basin.Diagnostics;
using Basin.Rashader;
using Basin.Rashader.Gl;
using Basin.Rashader.Vulkan;
using Basin.Render.Gl;
using Basin.Render.Vulkan;
using Basin.Scene;

namespace TinyComp;

internal sealed class ScreenShader : IDisposable
{
    private sealed record OutputFilter(IFrameFilter Installed, IRashaderFilter[] Links, IDisposable Disposer);

    private readonly IRenderer _renderer;
    private readonly string _rendererName;
    private readonly IAllocator? _allocator;
    private readonly BasinLogger _log;
    private readonly List<SceneOutput> _outputs = [];
    private readonly Dictionary<SceneOutput, OutputFilter> _filters = [];
    private IReadOnlyList<ShaderSetting> _entries = [];
    private bool _continuous;
    private bool _warnedRenderer;

    public ScreenShader(IRenderer renderer, string rendererName, IAllocator? allocator, BasinLogger log)
    {
        _renderer = renderer;
        _rendererName = rendererName;
        _allocator = allocator;
        _log = log;
    }

    public void Configure(Config config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var entries = Expand(config.Shaders);
        var changed = config.ShaderContinuous != _continuous || entries.Count != _entries.Count;
        for (var i = 0; !changed && i < entries.Count; i++)
        {
            changed = entries[i].Path != _entries[i].Path;
        }

        _entries = entries;
        _continuous = config.ShaderContinuous;
        if (changed)
        {
            DropFilters();
            return;
        }

        foreach (var filter in _filters.Values)
        {
            ApplyParameters(filter.Links);
        }
    }

    public void Apply(SceneOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (!_outputs.Contains(output))
        {
            _outputs.Add(output);
            output.Output.Destroyed += () => Remove(output);
        }

        Install(output);
    }

    public void Remove(SceneOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.SetFrameFilter(null);
        _ = _outputs.Remove(output);
        if (_filters.Remove(output, out var filter))
        {
            filter.Disposer.Dispose();
        }
    }

    public void Dispose() => DropFilters(forget: true);

    private void Install(SceneOutput output)
    {
        if (_entries.Count == 0)
        {
            output.SetFrameFilter(null);
            return;
        }

        if (_renderer is not (VulkanRenderer or GlRenderer))
        {
            if (!_warnedRenderer)
            {
                _log.Warn($"[effects] shader runs on the vulkan and gl renderers only, not {_rendererName}");
                _warnedRenderer = true;
            }

            return;
        }

        if (!_filters.TryGetValue(output, out var filter))
        {
            filter = Build(output);
            if (filter is null)
            {
                return;
            }

            ApplyParameters(filter.Links);
            _filters[output] = filter;
        }

        output.SetFrameFilter(filter.Installed, _allocator);
    }

    private OutputFilter? Build(SceneOutput output)
    {
        var mode = output.Output.CurrentMode;
        var settings = new RashaderFilterSettings
        {
            ContinuousRepaint = _continuous,
            VerticalScreen = mode.Height > mode.Width,
        };
        var links = new List<IRashaderFilter>(_entries.Count);
        foreach (var entry in _entries)
        {
            string? whyNot;
            IRashaderFilter? link = _renderer switch
            {
                VulkanRenderer vulkan => RashaderFilter.TryCreate(vulkan.Device, entry.Path, settings, out whyNot),
                GlRenderer gl => (IRashaderFilter?)RashaderGlFilter.TryCreate(gl.Device, entry.Path, settings, out whyNot),
                _ => throw new InvalidOperationException(),
            };
            if (link is null)
            {
                _log.Warn($"[effects] shader {entry.Path}: {whyNot}");
                continue;
            }

            links.Add(link);
        }

        return links.Count switch
        {
            0 => null,
            1 => new OutputFilter((IFrameFilter)links[0], [.. links], (IDisposable)links[0]),
            _ when links[0] is RashaderFilter =>
                Stacked(new RashaderFilterStack([.. links.Cast<RashaderFilter>()]), links),
            _ => Stacked(new RashaderGlFilterStack([.. links.Cast<RashaderGlFilter>()]), links),
        };
    }

    private static OutputFilter Stacked(IFrameFilter stack, List<IRashaderFilter> links) =>
        new(stack, [.. links], (IDisposable)stack);

    private void ApplyParameters(IRashaderFilter[] links)
    {
        var count = Math.Min(links.Length, _entries.Count);
        for (var i = 0; i < count; i++)
        {
            foreach (var (name, value) in _entries[i].Parameters)
            {
                if (!links[i].TrySetParameter(name, (float)value))
                {
                    _log.Warn($"[effects] shader {_entries[i].Path}: '{name}' names no parameter of this preset");
                }
            }
        }
    }

    private void DropFilters(bool forget = false)
    {
        foreach (var output in _outputs)
        {
            output.SetFrameFilter(null);
        }

        foreach (var filter in _filters.Values)
        {
            filter.Disposer.Dispose();
        }

        _filters.Clear();
        if (forget)
        {
            _outputs.Clear();
        }
    }

    private static IReadOnlyList<ShaderSetting> Expand(IReadOnlyList<ShaderSetting> entries)
    {
        if (entries.Count == 0)
        {
            return entries;
        }

        var expanded = new List<ShaderSetting>(entries.Count);
        foreach (var entry in entries)
        {
            expanded.Add(entry.Path.StartsWith("~/", StringComparison.Ordinal)
                ? entry with { Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), entry.Path[2..]) }
                : entry);
        }

        return expanded;
    }
}
