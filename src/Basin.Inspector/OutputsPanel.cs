using System.Numerics;
using Basin.Host;
using Basin.Scene;
using Hexa.NET.ImGui;
using Hexa.NET.ImPlot;
using ImGuiApi = Hexa.NET.ImGui.ImGui;

namespace Basin.Inspector;

/// <summary>
/// The Outputs panel. It shows the mode, scale, transform, timing and scanout state of each output, and plots the intervals between paints.
/// </summary>
public sealed unsafe class OutputsPanel : IInspectorPanel, IDisposable
{
    /// <summary>
    /// The number of intervals between paints that the panel keeps for each output.
    /// </summary>
    public const int Samples = 600;

    private readonly OutputDriver _driver;
    private readonly Func<long> _clock;
    private readonly Dictionary<OutputView, Timing> _timings = [];
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutputsPanel"/> class. It subscribes to the driver events until <see cref="Dispose"/>.
    /// </summary>
    /// <param name="driver">The driver whose outputs the panel shows.</param>
    /// <param name="clockNanos">The clock in nanoseconds, or <see langword="null"/> for <see cref="MonotonicClock.Nanos"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="driver"/> is <see langword="null"/>.</exception>
    public OutputsPanel(OutputDriver driver, Func<long>? clockNanos = null)
    {
        ArgumentNullException.ThrowIfNull(driver);
        _driver = driver;
        _clock = clockNanos ?? (() => MonotonicClock.Nanos);
        foreach (var view in driver.Views)
        {
            _timings[view] = new Timing();
        }

        driver.Added += OnAdded;
        driver.Removed += OnRemoved;
        driver.Painted += OnPainted;
        driver.ScanoutChanged += OnScanoutChanged;
    }

    /// <inheritdoc/>
    public ReadOnlySpan<byte> Name => "Outputs"u8;

    /// <summary>
    /// Gets or sets the rate in frames per second at which the panel redraws while it is shown. The default is 10.
    /// </summary>
    public int RefreshHz { get; set; } = 10;

    /// <inheritdoc/>
    public void Draw(ref InspectorDraw draw)
    {
        draw.RequestRefresh(RefreshHz);
        var text = draw.Text;
        var views = _driver.Views;
        for (var i = 0; i < views.Count; i++)
        {
            var view = views[i];
            var output = view.Output;
            var mode = output.CurrentMode;
            ImGuiApi.PushID(i);
            var label = text.Format($"{output.Name}  {mode.Width}x{mode.Height}@{mode.RefreshMilliHz / 1000.0:0.##}Hz");
            if (ImGuiApi.CollapsingHeader(label, ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGuiApi.TextUnformatted(text.Format(
                    $"scale {output.Scale:0.###}  transform {output.Transform}  vrr {(output.AdaptiveSync ? "on" : "off")}  box {view.Box.X},{view.Box.Y} {view.Box.Width}x{view.Box.Height}"));
                if (view.Scheduler is { } scheduler)
                {
                    ImGuiApi.TextUnformatted(text.Format(
                        $"render-ahead {scheduler.RenderAheadMillis} ms  lead {scheduler.EffectiveLeadNanos / 1000} us  missed vblanks {scheduler.MissedVblanks}"));
                }

                if (_timings.TryGetValue(view, out var timing))
                {
                    ImGuiApi.TextUnformatted(text.Format(
                        $"scanout {Describe(timing.Scanout)}  planes {view.Scene?.OffloadedLayers ?? 0}  painted {timing.Count}  last {timing.Last:0.00} ms"));
                    if (ImPlot.BeginPlot(text.Format($"##frames{i}"), new Vector2(-1, 140), ImPlotFlags.NoMenus | ImPlotFlags.NoLegend))
                    {
                        ImPlot.SetupAxes("frame"u8, "ms"u8, ImPlotAxisFlags.NoTickLabels, ImPlotAxisFlags.AutoFit);
                        ImPlot.SetupAxisLimits(ImAxis.X1, 0, Samples, ImPlotCond.Always);
                        fixed (float* values = timing.Intervals)
                        {
                            ImPlot.PlotLine("ms"u8, values, Math.Min(timing.Count, Samples), 1, 0, ImPlotLineFlags.None, timing.Count >= Samples ? timing.Next : 0);
                        }

                        ImPlot.EndPlot();
                    }
                }
            }

            ImGuiApi.PopID();
        }
    }

    /// <summary>
    /// Unsubscribes from the driver events and drops the timing samples. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _driver.Added -= OnAdded;
        _driver.Removed -= OnRemoved;
        _driver.Painted -= OnPainted;
        _driver.ScanoutChanged -= OnScanoutChanged;
        _timings.Clear();
    }

    private static string Describe(ScanoutChoice? choice) => choice switch
    {
        ScanoutChoice.DeviceBuffers => "device buffers",
        ScanoutChoice.DumbLinear => "dumb linear",
        ScanoutChoice.RefusedByPlane => "refused by plane",
        _ => "composited",
    };

    private void OnAdded(OutputView view) => _timings[view] = new Timing();

    private void OnRemoved(OutputView view) => _timings.Remove(view);

    private void OnScanoutChanged(OutputView view, ScanoutChoice choice)
    {
        if (_timings.TryGetValue(view, out var timing))
        {
            timing.Scanout = choice;
        }
    }

    private void OnPainted(OutputView view)
    {
        if (!_timings.TryGetValue(view, out var timing))
        {
            return;
        }

        var now = _clock();
        if (timing.Previous != 0)
        {
            timing.Last = (now - timing.Previous) / 1_000_000f;
            timing.Intervals[timing.Next] = timing.Last;
            timing.Next = (timing.Next + 1) % Samples;
            timing.Count++;
        }

        timing.Previous = now;
    }

    private sealed class Timing
    {
        public readonly float[] Intervals = new float[Samples];
        public int Next;
        public int Count;
        public long Previous;
        public float Last;
        public ScanoutChoice? Scanout;
    }
}
