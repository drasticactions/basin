using Basin.Capabilities;

namespace Basin.Frames.Metacity;

public readonly record struct MetacityFrameInput(FrameState State, FrameInteraction Interaction)
{
    internal MetacityFrameType Type => State.Kind switch
    {
        FrameKind.Dialog => MetacityFrameType.Dialog,
        FrameKind.ModalDialog => MetacityFrameType.ModalDialog,
        FrameKind.Utility => MetacityFrameType.Utility,
        FrameKind.Menu => MetacityFrameType.Menu,
        FrameKind.Border => MetacityFrameType.Border,
        FrameKind.Attached => MetacityFrameType.Attached,
        _ => MetacityFrameType.Normal,
    };

    internal MetacityFrameStateKind FrameStateKind
    {
        get
        {
            var tiled = State.Tiled;
            if (tiled == FrameTiling.Left)
            {
                return State.Shaded ? MetacityFrameStateKind.TiledLeftAndShaded : MetacityFrameStateKind.TiledLeft;
            }

            if (tiled == FrameTiling.Right)
            {
                return State.Shaded ? MetacityFrameStateKind.TiledRightAndShaded : MetacityFrameStateKind.TiledRight;
            }

            if (State.Maximized)
            {
                return State.Shaded ? MetacityFrameStateKind.MaximizedAndShaded : MetacityFrameStateKind.Maximized;
            }

            return State.Shaded ? MetacityFrameStateKind.Shaded : MetacityFrameStateKind.Normal;
        }
    }

    internal MetacityFocus Focus => State.Active ? MetacityFocus.Yes : MetacityFocus.No;

    internal bool Allows(FrameCapabilities capability) => (State.Capabilities & capability) != 0;

    internal bool AtEdge => State.Maximized || State.Tiled != FrameTiling.None;

    internal MetacityButtonState ButtonState(MetacityButtonType type)
    {
        var part = MetacityFrameGeometry.PartOf(type);
        if (part == FramePart.None)
        {
            return MetacityButtonState.Normal;
        }

        if (Interaction.Pressed == part)
        {
            return MetacityButtonState.Pressed;
        }

        return Interaction.Hot == part ? MetacityButtonState.Prelight : MetacityButtonState.Normal;
    }
}
