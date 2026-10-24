using System.Numerics;
using System.Runtime.InteropServices;
using Basin.Diagnostics;
using Hexa.NET.ImGui;
using Hexa.NET.ImPlot;
using ImGuiApi = Hexa.NET.ImGui.ImGui;

namespace Basin.Inspector;

/// <summary>
/// The Memory panel. It shows the <see cref="Basin.Diagnostics.BasinCounters"/> totals and census, the GC collections, the bytes allocated between draws and the open descriptor count.
/// </summary>
public sealed unsafe class MemoryPanel : IInspectorPanel
{
    /// <summary>
    /// The number of draws that the allocation plot keeps.
    /// </summary>
    public const int Samples = 240;

    private const int ORdOnly = 0;
    private const int ODirectory = 0x10000;
    private const int OCloexec = 0x80000;
    private const long SysGetdents64 = 217;
    private const long SlowNanos = 1_000_000_000;

    private readonly Dictionary<string, int> _census = new(StringComparer.Ordinal);
    private readonly float[] _allocated = new float[Samples];
    private readonly byte[] _entries = GC.AllocateArray<byte>(16 * 1024, pinned: true);
    private long _lastAllocated = -1;
    private long _lastSlow;
    private int _next;
    private int _samples;
    private int _fds = -1;

    /// <inheritdoc/>
    public ReadOnlySpan<byte> Name => "Memory"u8;

    /// <summary>
    /// Gets or sets the rate in frames per second at which the panel redraws while it is shown. The default is 4. The census and the descriptor count refresh at 1 Hz.
    /// </summary>
    public int RefreshHz { get; set; } = 4;

    /// <summary>
    /// Gets the number of open file descriptors of the process at the last count. It is -1 before the first draw or when the count failed.
    /// </summary>
    public int OpenDescriptors => _fds;

    /// <inheritdoc/>
    public void Draw(ref InspectorDraw draw)
    {
        draw.RequestRefresh(RefreshHz);
        var text = draw.Text;
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        if (_lastAllocated >= 0)
        {
            _allocated[_next] = allocated - _lastAllocated;
            _next = (_next + 1) % Samples;
            _samples++;
        }

        _lastAllocated = allocated;
        if (_lastSlow == 0 || draw.NowNanos - _lastSlow >= SlowNanos)
        {
            _lastSlow = draw.NowNanos;
            _fds = CountDescriptors();
            if (BasinCounters.Enabled)
            {
                BasinCounters.SnapshotCensus(_census);
            }
        }

        if (BasinCounters.Enabled)
        {
            ImGuiApi.TextUnformatted(text.Format($"live objects {BasinCounters.LiveObjects}  pending frees {BasinCounters.PendingFrees}"));
        }
        else
        {
            ImGuiApi.TextUnformatted("Counters are off in this build."u8);
        }

        ImGuiApi.TextUnformatted(text.Format(
            $"collections gen0 {GC.CollectionCount(0)}  gen1 {GC.CollectionCount(1)}  gen2 {GC.CollectionCount(2)}  open fds {_fds}"));
        ImGuiApi.TextUnformatted(text.Format($"allocated on this thread {allocated / 1024} KiB"));
        if (ImPlot.BeginPlot("##allocated"u8, new Vector2(-1, 120), ImPlotFlags.NoMenus | ImPlotFlags.NoLegend))
        {
            ImPlot.SetupAxes("draw"u8, "bytes"u8, ImPlotAxisFlags.NoTickLabels, ImPlotAxisFlags.AutoFit);
            ImPlot.SetupAxisLimits(ImAxis.X1, 0, Samples, ImPlotCond.Always);
            fixed (float* values = _allocated)
            {
                ImPlot.PlotBars("bytes"u8, values, Math.Min(_samples, Samples), 0.67, 0, ImPlotBarsFlags.None, _samples >= Samples ? _next : 0);
            }

            ImPlot.EndPlot();
        }

        if (!BasinCounters.Enabled || _census.Count == 0)
        {
            return;
        }

        if (ImGuiApi.BeginTable("census"u8, 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY, new Vector2(0, 0)))
        {
            ImGuiApi.TableSetupColumn("file"u8);
            ImGuiApi.TableSetupColumn("live"u8, ImGuiTableColumnFlags.WidthFixed, 80);
            ImGuiApi.TableHeadersRow();
            foreach (var (file, count) in _census)
            {
                if (count == 0)
                {
                    continue;
                }

                ImGuiApi.TableNextRow();
                ImGuiApi.TableNextColumn();
                ImGuiApi.TextUnformatted(text.Encode(Path.GetFileName(file.AsSpan())));
                ImGuiApi.TableNextColumn();
                ImGuiApi.TextUnformatted(text.Format($"{count}"));
            }

            ImGuiApi.EndTable();
        }
    }

    private int CountDescriptors()
    {
        int fd;
        fixed (byte* path = "/proc/self/fd"u8)
        {
            fd = open(path, ORdOnly | ODirectory | OCloexec);
        }

        if (fd < 0)
        {
            return -1;
        }

        var count = 0;
        fixed (byte* entries = _entries)
        {
            while (true)
            {
                var read = syscall(SysGetdents64, fd, (nint)entries, _entries.Length);
                if (read <= 0)
                {
                    break;
                }

                for (var offset = 0L; offset < read;)
                {
                    var length = *(ushort*)(entries + offset + 16);
                    var name = entries + offset + 19;
                    if (!(name[0] == '.' && (name[1] == 0 || (name[1] == '.' && name[2] == 0))))
                    {
                        count++;
                    }

                    offset += length;
                }
            }
        }

        _ = close(fd);
        return Math.Max(0, count - 1);
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int open(byte* path, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern long syscall(long number, int fd, nint buffer, int count);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);
}
