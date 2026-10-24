using System.Numerics;

namespace Basin.Scene3D;

/// <summary>
/// A node hierarchy with a rest pose, and the joints that skin a mesh with their inverse bind matrices.
/// </summary>
public sealed class Skeleton
{
    /// <summary>
    /// The largest joint count that a pass uploads in its palette. The glTF loader refuses a skin with more.
    /// </summary>
    public const int MaxJoints = 48;

    /// <summary>
    /// Initializes a new instance of the <see cref="Skeleton"/> class. It keeps the arrays and does not copy them.
    /// </summary>
    /// <param name="names">The node names, one for each node.</param>
    /// <param name="parents">The parent index of each node, or -1 for a root.</param>
    /// <param name="translations">The rest translation of each node, relative to its parent.</param>
    /// <param name="rotations">The rest rotation of each node.</param>
    /// <param name="scales">The rest scale of each node.</param>
    /// <param name="joints">The node index of each joint.</param>
    /// <param name="inverseBinds">The inverse bind matrix of each joint.</param>
    /// <exception cref="ArgumentException">The node arrays differ in length, a joint has no inverse bind matrix, or the hierarchy has a cycle.</exception>
    public Skeleton(
        string[] names,
        int[] parents,
        Vector3[] translations,
        Quaternion[] rotations,
        Vector3[] scales,
        int[] joints,
        Matrix4x4[] inverseBinds)
    {
        var count = names.Length;
        if (parents.Length != count || translations.Length != count || rotations.Length != count || scales.Length != count)
        {
            throw new ArgumentException("Every node array must have one entry per node.", nameof(parents));
        }

        if (joints.Length != inverseBinds.Length)
        {
            throw new ArgumentException("Every joint needs an inverse bind matrix.", nameof(inverseBinds));
        }

        Names = names;
        Parents = parents;
        Translations = translations;
        Rotations = rotations;
        Scales = scales;
        Joints = joints;
        InverseBinds = inverseBinds;
        Order = TopologicalOrder(parents);
    }

    /// <summary>
    /// Gets the node names.
    /// </summary>
    public string[] Names { get; }

    /// <summary>
    /// Gets the parent index of each node. A root has -1.
    /// </summary>
    public int[] Parents { get; }

    /// <summary>
    /// Gets the rest translation of each node, relative to its parent.
    /// </summary>
    public Vector3[] Translations { get; }

    /// <summary>
    /// Gets the rest rotation of each node.
    /// </summary>
    public Quaternion[] Rotations { get; }

    /// <summary>
    /// Gets the rest scale of each node.
    /// </summary>
    public Vector3[] Scales { get; }

    /// <summary>
    /// Gets the node index of each joint. A skinned vertex refers to a joint by its index in this array.
    /// </summary>
    public int[] Joints { get; }

    /// <summary>
    /// Gets the inverse bind matrix of each joint.
    /// </summary>
    public Matrix4x4[] InverseBinds { get; }

    /// <summary>
    /// Gets the node indices with each parent before its children.
    /// </summary>
    public int[] Order { get; }

    /// <summary>
    /// Gets the number of nodes.
    /// </summary>
    public int NodeCount => Names.Length;

    /// <summary>
    /// Finds a node by name.
    /// </summary>
    /// <param name="name">The node name, compared exactly.</param>
    /// <returns>The node index, or -1 when no node has the name.</returns>
    public int IndexOf(string name) => Array.IndexOf(Names, name);

    private static int[] TopologicalOrder(int[] parents)
    {
        var order = new List<int>(parents.Length);
        var placed = new bool[parents.Length];
        while (order.Count < parents.Length)
        {
            var progressed = false;
            for (var i = 0; i < parents.Length; i++)
            {
                if (!placed[i] && (parents[i] < 0 || placed[parents[i]]))
                {
                    placed[i] = true;
                    order.Add(i);
                    progressed = true;
                }
            }

            if (!progressed)
            {
                throw new ArgumentException("The node hierarchy has a cycle.", nameof(parents));
            }
        }

        return [.. order];
    }
}
