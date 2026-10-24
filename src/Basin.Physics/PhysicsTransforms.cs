using Basin.Scene;

namespace Basin.Physics;

/// <summary>
/// Writes the angle of a <see cref="PhysicsPose"/> into a window's <see cref="TransformStack"/>.
/// </summary>
public static class PhysicsTransforms
{
    /// <summary>
    /// The node name that <see cref="Apply(TransformStack, in PhysicsPose)"/> uses.
    /// </summary>
    public const string Name = "physics";

    /// <summary>
    /// Writes the rotation of a pose into the node named <see cref="Name"/>. At an angle of zero it removes the node.
    /// </summary>
    /// <param name="stack">The transform stack of the window.</param>
    /// <param name="pose">The pose to write. Only the angle and the pivot are used.</param>
    /// <returns><see langword="true"/> when the stack changed.</returns>
    public static bool Apply(TransformStack stack, in PhysicsPose pose) => Apply(stack, pose, Name);

    /// <summary>
    /// Writes the rotation of a pose into a named node at the <see cref="TransformStack.ZOrder.Transform2D"/> order. At an angle of zero it removes the node, so a window at rest keeps no transform.
    /// </summary>
    /// <param name="stack">The transform stack of the window.</param>
    /// <param name="pose">The pose to write. Only the angle and the pivot are used.</param>
    /// <param name="name">The node name. It must not be null or empty.</param>
    /// <returns><see langword="true"/> when the stack changed, and <see langword="false"/> when the node already held the same matrix or was already absent.</returns>
    public static bool Apply(TransformStack stack, in PhysicsPose pose, string name)
    {
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (pose.Angle == 0)
        {
            return stack.Remove(name);
        }

        var node = stack.Get(name) ?? stack.Add(TransformStack.ZOrder.Transform2D, name);
        var matrix = pose.Rotation;
        if (node.Matrix == matrix)
        {
            return false;
        }

        node.Matrix = matrix;
        return true;
    }
}
