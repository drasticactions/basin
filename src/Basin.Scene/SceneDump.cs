using Basin.Capabilities;
using System.Globalization;
using System.Text;

namespace Basin.Scene;

public static class SceneDump
{
    public static void Write(SceneNode root, Action<string> line)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(line);
        foreach (var node in Nodes(root))
        {
            line(Format(node));
        }
    }

    public static IReadOnlyList<SceneNodeInfo> Nodes(SceneNode root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var nodes = new List<SceneNodeInfo>();
        Collect(root, 0, nodes);
        return nodes;
    }

    public static string Format(in SceneNodeInfo node)
    {
        var text = new StringBuilder();
        text.Append(' ', node.Depth * 2);
        text.Append(node.Kind);
        if (node.Name is { } name)
        {
            text.Append(" \"").Append(name).Append('"');
        }

        text.Append(CultureInfo.InvariantCulture, $" at={node.X},{node.Y}");
        if (!node.Enabled)
        {
            text.Append(" disabled");
        }

        text.Append(node.Details);
        return text.ToString();
    }

    public static string PathOf(SceneNode? node)
    {
        if (node is null)
        {
            return "(scene)";
        }

        var parts = new List<string>();
        for (var current = node; current is not null; current = current.Parent)
        {
            parts.Add(current.DebugName ?? Kind(current));
        }

        parts.Reverse();
        return string.Join('/', parts);
    }

    private static void Collect(SceneNode node, int depth, List<SceneNodeInfo> nodes)
    {
        var text = new StringBuilder();
        if (node.IsClipped)
        {
            var clip = node.ClipBox;
            text.Append(CultureInfo.InvariantCulture, $" clip={clip.X},{clip.Y},{clip.Width}x{clip.Height}");
        }

        switch (node)
        {
            case SceneBuffer buffer:
                Describe(buffer, text);
                break;
            case SceneRect rect:
                text.Append(CultureInfo.InvariantCulture, $" size={rect.Width}x{rect.Height} color={rect.Color.R:F2},{rect.Color.G:F2},{rect.Color.B:F2},{rect.Color.A:F2}");
                break;
            case SceneTransform transform:
                Describe(transform, text);
                break;
            case SceneTree tree when tree.Alpha < 1f:
                text.Append(CultureInfo.InvariantCulture, $" alpha={tree.Alpha:F3}");
                break;
        }

        var (width, height) = node switch
        {
            SceneBuffer buffer => buffer.Size,
            SceneRect rect => (rect.Width, rect.Height),
            _ => (0, 0),
        };
        nodes.Add(new SceneNodeInfo(
            depth, Kind(node), node.DebugName, PathOf(node), node.X, node.Y, width, height, node.Enabled, text.ToString()));
        if (node is SceneTree children)
        {
            foreach (var child in children.Children)
            {
                Collect(child, depth + 1, nodes);
            }
        }
    }

    private static void Describe(SceneBuffer buffer, StringBuilder text)
    {
        var (width, height) = buffer.Size;
        text.Append(CultureInfo.InvariantCulture, $" size={width}x{height}");
        if (buffer.Buffer is { } content)
        {
            text.Append(CultureInfo.InvariantCulture, $" buffer={content.Width}x{content.Height}");
        }

        if (!buffer.SourceBox.IsEmpty)
        {
            var source = buffer.SourceBox;
            text.Append(CultureInfo.InvariantCulture, $" src={source.X:F1},{source.Y:F1},{source.Width:F1}x{source.Height:F1}");
        }

        if (buffer.BackdropOnly)
        {
            text.Append(" backdrop-only");
        }

        if (buffer.IsOpaque)
        {
            text.Append(" opaque");
        }

        if (buffer.TextureShader is not null)
        {
            text.Append(" shader");
        }

        if (buffer.InputSurface is not null)
        {
            text.Append(" surface");
        }

        if (buffer.HasActiveBackdrop)
        {
            var extents = buffer.BackdropRegion!.Extents;
            text.Append(CultureInfo.InvariantCulture,
                $" backdrop={buffer.BackdropEffect!.GetType().Name} region={extents.X1},{extents.Y1},{extents.X2 - extents.X1}x{extents.Y2 - extents.Y1}");
        }
    }

    private static void Describe(SceneTransform transform, StringBuilder text)
    {
        if (transform.Alpha < 1f)
        {
            text.Append(CultureInfo.InvariantCulture, $" alpha={transform.Alpha:F3}");
        }

        if (!transform.Matrix.IsIdentity)
        {
            text.Append(" matrix");
        }

        if (transform.Deformer is { } deformer)
        {
            var child = transform.ChildBounds();
            var hull = deformer.MapBounds(child);
            text.Append(" deformer=").Append(deformer.GetType().Name);
            text.Append(CultureInfo.InvariantCulture, $" child={child.X},{child.Y},{child.Width}x{child.Height} hull={hull.X},{hull.Y},{hull.Width}x{hull.Height}");
        }
    }

    private static string Kind(SceneNode node) => node switch
    {
        SceneBuffer => "buffer",
        SceneRect => "rect",
        SceneMesh => "mesh",
        SceneShader => "shader",
        SceneTransform => "transform",
        SceneTree => "tree",
        _ => node.GetType().Name,
    };
}
