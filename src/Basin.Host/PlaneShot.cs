using Basin.Diagnostics;
using Basin.Scene;

namespace Basin.Host;

public static class PlaneShot
{
    public static int Write(
        OutputView view,
        IRenderer renderer,
        string prefix,
        Scene.Scene? scene = null,
        IReadOnlyList<PlaneShotChrome>? chrome = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentException.ThrowIfNullOrEmpty(prefix);

        var written = 0;
        if (view.LastPresentedBuffer is { IsDestroyed: false } primary &&
            BufferCapture.TryWritePng(primary, renderer, $"{prefix}.primary.png"))
        {
            BasinReport.Line($"PLANE primary {primary.Width}x{primary.Height} {prefix}.primary.png");
            written++;
        }

        if (view.Scene?.PresentedLayers is { } layers)
        {
            for (var i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                var path = $"{prefix}.layer{i}.png";
                if (layer.Buffer is { IsDestroyed: false } buffer && BufferCapture.TryWritePng(buffer, renderer, path))
                {
                    BasinReport.Line(
                        $"PLANE layer{i} accepted={layer.Accepted} dst={layer.DstBox} src={layer.SrcBox} "
                        + $"alpha={layer.Alpha:F2} opaque={layer.Opaque} {buffer.Width}x{buffer.Height} {path}");
                    written++;
                }
            }
        }

        if (view.Output is IHardwareCursor cursor &&
            cursor.TryPresentedCursor(out var sprite, out var where) &&
            BufferCapture.TryWritePng(sprite, renderer, $"{prefix}.cursor.png"))
        {
            BasinReport.Line($"PLANE cursor dst={where} {prefix}.cursor.png");
            written++;
        }

        var named = new HashSet<SceneBuffer>();
        var seen = new HashSet<IBuffer>();
        if (chrome is not null)
        {
            foreach (var entry in chrome)
            {
                if (entry.Node is { } node)
                {
                    named.Add(node);
                }

                var buffer = entry.Buffer ?? entry.Node?.Buffer;
                if (buffer is not null)
                {
                    seen.Add(buffer);
                }

                if (WriteChrome(entry.Name, buffer, entry.Node, entry.Detail, renderer, prefix))
                {
                    written++;
                }
            }
        }

        if (scene is not null)
        {
            var index = 0;
            foreach (var node in Backdrops(scene.Root))
            {
                if (named.Contains(node) || node.Buffer is not { } buffer || !seen.Add(buffer))
                {
                    continue;
                }

                if (WriteChrome($"backdrop{index++}", buffer, node, null, renderer, prefix))
                {
                    written++;
                }
            }
        }

        BasinReport.Line($"PLANESHOT {prefix} images={written}");
        return written;
    }

    private static bool WriteChrome(
        string name, IBuffer? buffer, SceneBuffer? node, string? detail, IRenderer renderer, string prefix)
    {
        if (buffer is not { IsDestroyed: false })
        {
            return false;
        }

        var path = $"{prefix}.{name}.png";
        if (!BufferCapture.TryWritePng(buffer, renderer, path))
        {
            return false;
        }

        var state = node is null
            ? string.Empty
            : $" opaque={node.IsOpaque} backdrop={(node.BackdropEffect is null ? "none" : "live")}";
        var extra = detail is { Length: > 0 } ? $" {detail}" : string.Empty;
        BasinReport.Line($"PLANE chrome {name} {buffer.Width}x{buffer.Height} format={buffer.Format}{state}{extra} {path}");
        return true;
    }

    private static IEnumerable<SceneBuffer> Backdrops(SceneTree tree)
    {
        foreach (var child in tree.Children)
        {
            if (child is SceneBuffer { BackdropEffect: not null, IsDestroyed: false } buffer)
            {
                yield return buffer;
            }
            else if (child is SceneTree subtree)
            {
                foreach (var nested in Backdrops(subtree))
                {
                    yield return nested;
                }
            }
        }
    }
}
