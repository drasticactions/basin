using System.Globalization;
using System.Numerics;
using Basin.Inspector;
using Basin.Physics;
using Hexa.NET.ImGui;
using Hexa.NET.ImGuizmo;
using ImGuiApi = Hexa.NET.ImGui.ImGui;

namespace DungeonWm;

internal sealed unsafe class InspectorPosePanel : IInspectorPanel
{
    private const float Margin = 140;

    private readonly DungeonWm _comp;
    private readonly float[] _view = new float[16];
    private readonly float[] _projection = new float[16];
    private readonly float[] _model = new float[16];
    private readonly List<(string Name, PhysicsBody3D Body)> _targets = [];
    private PhysicsBody3D _selected;
    private string _selectedName = string.Empty;
    private ImGuizmoOperation _operation = ImGuizmoOperation.TranslateX | ImGuizmoOperation.TranslateY | ImGuizmoOperation.TranslateZ;

    internal InspectorPosePanel(DungeonWm comp)
    {
        _comp = comp;
    }

    public ReadOnlySpan<byte> Name => "Pose"u8;

    public void Draw(ref InspectorDraw draw)
    {
        var text = draw.Text;
        Collect();
        if (_targets.Count == 0)
        {
            ImGuiApi.TextUnformatted("nothing to pose: pull a window off its wall, or add props"u8);
            return;
        }

        if (!Contains(_selected))
        {
            (_selectedName, _selected) = _targets[0];
        }

        if (ImGuiApi.BeginCombo("body"u8, text.Encode(_selectedName)))
        {
            foreach (var (name, body) in _targets)
            {
                if (ImGuiApi.Selectable(text.Encode(name), body == _selected))
                {
                    (_selectedName, _selected) = (name, body);
                }
            }

            ImGuiApi.EndCombo();
        }

        Operation("move"u8, ImGuizmoOperation.TranslateX | ImGuizmoOperation.TranslateY | ImGuizmoOperation.TranslateZ);
        ImGuiApi.SameLine();
        Operation("turn"u8, ImGuizmoOperation.RotateX | ImGuizmoOperation.RotateY | ImGuizmoOperation.RotateZ);
        var pose = _selected.Pose;
        ImGuiApi.TextUnformatted(text.Format(
            $"at {pose.Position.X:0.00},{pose.Position.Y:0.00},{pose.Position.Z:0.00}  speed {_selected.Velocity.Length():0.00}"));
        Gizmo(draw, pose);
    }

    internal void ReportTarget()
    {
        Collect();
        if (_targets.Count == 0)
        {
            _comp.Report("POSE none");
            return;
        }

        if (!Contains(_selected))
        {
            (_selectedName, _selected) = _targets[0];
        }

        var pose = _selected.Pose;
        if (_comp.InspectorOutputView is { } view && _comp.ViewFor(view) is { } world &&
            world.TryProject(pose.Position, out var x, out var y, out _))
        {
            _comp.Report(string.Create(CultureInfo.InvariantCulture,
                $"POSE {_selectedName} at={pose.Position.X:F2},{pose.Position.Y:F2},{pose.Position.Z:F2} screen={x:F0},{y:F0}"));
            return;
        }

        _comp.Report(string.Create(CultureInfo.InvariantCulture,
            $"POSE {_selectedName} at={pose.Position.X:F2},{pose.Position.Y:F2},{pose.Position.Z:F2} screen=none"));
    }

    private void Operation(ReadOnlySpan<byte> label, ImGuizmoOperation operation)
    {
        if (ImGuiApi.RadioButton(label, _operation == operation))
        {
            _operation = operation;
        }
    }

    private void Gizmo(InspectorDraw draw, PhysicsPose3D pose)
    {
        if (draw.Overlay.Output is not { } output || _comp.ViewFor(output) is not { } world ||
            _comp.PhysicsWorld is not { } physics || !world.TryProject(pose.Position, out var x, out var y, out _))
        {
            return;
        }

        var left = (float)(x - output.Box.X);
        var top = (float)(y - output.Box.Y);
        var flags = ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings
                    | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNav
                    | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoDocking;
        ImGuiApi.SetNextWindowPos(new Vector2(left - Margin, top - Margin), ImGuiCond.Always);
        ImGuiApi.SetNextWindowSize(new Vector2(Margin * 2, Margin * 2), ImGuiCond.Always);
        ImGuiApi.Begin("##pose-gizmo"u8, flags);
        ImGuiP.BringWindowToDisplayFront(ImGuiP.GetCurrentWindow());
        ImGuizmo.SetDrawlist(ImGuiApi.GetWindowDrawList());
        ImGuizmo.SetOrthographic(false);
        ImGuizmo.SetRect(0, 0, draw.Width, draw.Height);
        Copy(world.WorldToCamera, _view);
        Copy(world.Projection, _projection);
        if (!ImGuizmo.IsUsing())
        {
            Copy(Matrix4x4.CreateFromQuaternion(pose.Orientation) * Matrix4x4.CreateTranslation(pose.Position), _model);
        }

        bool changed;
        fixed (float* view = _view)
        fixed (float* projection = _projection)
        fixed (float* model = _model)
        {
            changed = ImGuizmo.Manipulate(view, projection, _operation, ImGuizmoMode.World, model);
        }

        ImGuiApi.End();
        if (!changed)
        {
            return;
        }

        var matrix = Read(_model);
        if (!Matrix4x4.Decompose(matrix, out _, out var orientation, out var position))
        {
            return;
        }

        physics.World.Teleport(_selected, new PhysicsPose3D(position, Quaternion.Normalize(orientation)));
        physics.World.SetVelocity(_selected, Vector3.Zero, Vector3.Zero);
        _comp.Repaint();
    }

    private void Collect()
    {
        _targets.Clear();
        if (_comp.PhysicsWorld is not { } physics)
        {
            return;
        }

        foreach (var (window, body) in physics.Bodies)
        {
            _targets.Add((window.AppId.Length > 0 ? window.AppId : $"window {window.ToplevelId}", body));
        }

        foreach (var (prop, body) in physics.Props)
        {
            _targets.Add((prop.Piece, body));
        }
    }

    private bool Contains(PhysicsBody3D body)
    {
        foreach (var (_, candidate) in _targets)
        {
            if (candidate == body)
            {
                return true;
            }
        }

        return false;
    }

    private static void Copy(in Matrix4x4 matrix, float[] into)
    {
        into[0] = matrix.M11;
        into[1] = matrix.M12;
        into[2] = matrix.M13;
        into[3] = matrix.M14;
        into[4] = matrix.M21;
        into[5] = matrix.M22;
        into[6] = matrix.M23;
        into[7] = matrix.M24;
        into[8] = matrix.M31;
        into[9] = matrix.M32;
        into[10] = matrix.M33;
        into[11] = matrix.M34;
        into[12] = matrix.M41;
        into[13] = matrix.M42;
        into[14] = matrix.M43;
        into[15] = matrix.M44;
    }

    private static Matrix4x4 Read(float[] values) => new(
        values[0], values[1], values[2], values[3],
        values[4], values[5], values[6], values[7],
        values[8], values[9], values[10], values[11],
        values[12], values[13], values[14], values[15]);
}
