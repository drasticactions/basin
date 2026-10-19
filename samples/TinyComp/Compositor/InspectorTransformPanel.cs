using System.Numerics;
using Basin.Inspector;
using Hexa.NET.ImGui;
using Hexa.NET.ImGuizmo;
using ImGuiApi = Hexa.NET.ImGui.ImGui;

namespace TinyComp;

internal sealed unsafe class InspectorTransformPanel : IInspectorPanel
{
    private const float Margin = 120;

    private readonly TinyComp _comp;
    private readonly float[] _view = new float[16];
    private readonly float[] _projection = new float[16];
    private readonly float[] _model = new float[16];
    private TinyComp.IGrabTarget? _selected;
    private ImGuizmoOperation _operation = ImGuizmoOperation.TranslateX | ImGuizmoOperation.TranslateY | ImGuizmoOperation.RotateZ;
    private double _scale = 1;
    private double _sentAngle = double.NaN;
    private bool _over;
    private bool _using;

    internal InspectorTransformPanel(TinyComp comp)
    {
        _comp = comp;
        _view[0] = _view[5] = _view[10] = _view[15] = 1;
    }

    public ReadOnlySpan<byte> Name => "Transform"u8;

    public void Draw(ref InspectorDraw draw)
    {
        var text = draw.Text;
        var targets = _comp.GrabTargets;
        if (_selected is null || !targets.Contains(_selected))
        {
            _selected = _comp.FocusedTarget ?? (targets.Count > 0 ? targets[0] : null);
        }

        if (ImGuiApi.BeginCombo("window"u8, _selected is null ? "none"u8 : text.Encode(_comp.TargetName(_selected))))
        {
            foreach (var target in targets)
            {
                if (ImGuiApi.Selectable(text.Encode(_comp.TargetName(target)), ReferenceEquals(target, _selected)))
                {
                    _selected = target;
                    _sentAngle = double.NaN;
                    _scale = _comp.InspectorScaleOf(target);
                }
            }

            ImGuiApi.EndCombo();
        }

        Operation("move"u8, ImGuizmoOperation.TranslateX | ImGuizmoOperation.TranslateY);
        ImGuiApi.SameLine();
        Operation("rotate"u8, ImGuizmoOperation.RotateZ);
        ImGuiApi.SameLine();
        Operation("scale"u8, ImGuizmoOperation.ScaleXu);
        if (_selected is not { } window)
        {
            return;
        }

        var angle = _comp.AngleOf(window);
        ImGuiApi.TextUnformatted(text.Format(
            $"at {window.X},{window.Y}  {window.GeometrySize.Width}x{window.GeometrySize.Height}  angle {angle * 180 / Math.PI:0.0}  scale {_scale:0.00}"));
        if (ImGuiApi.Button("reset"u8))
        {
            _comp.RotateWindow(window, 0, relative: false);
            _scale = 1;
            _comp.SetInspectorScale(window, 1);
        }

        ImGuiApi.TextUnformatted(text.Format($"gizmo {(_using ? "dragging" : _over ? "hovered" : "idle")}"));
        Gizmo(draw, window, angle);
    }

    private void Operation(ReadOnlySpan<byte> label, ImGuizmoOperation operation)
    {
        if (ImGuiApi.RadioButton(label, (_operation & operation) == operation))
        {
            _operation = operation;
        }
    }

    private void Gizmo(InspectorDraw draw, TinyComp.IGrabTarget window, double angle)
    {
        if (draw.Overlay.Output is not { } view)
        {
            return;
        }

        var (width, height) = window.GeometrySize;
        var left = window.X - view.Box.X;
        var top = window.Y - view.Box.Y;
        var centerX = left + (width / 2f);
        var centerY = top + (height / 2f);

        var flags = ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings
                    | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNav
                    | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoDocking;
        ImGuiApi.SetNextWindowPos(new Vector2(centerX - Margin, centerY - Margin), ImGuiCond.Always);
        ImGuiApi.SetNextWindowSize(new Vector2(Margin * 2, Margin * 2), ImGuiCond.Always);
        ImGuiApi.Begin("##transform-gizmo"u8, flags);
        ImGuiP.BringWindowToDisplayFront(ImGuiP.GetCurrentWindow());
        ImGuizmo.SetDrawlist(ImGuiApi.GetWindowDrawList());
        ImGuizmo.SetOrthographic(true);
        ImGuizmo.SetRect(0, 0, draw.Width, draw.Height);
        Ortho(_projection, draw.Width, draw.Height);
        if (!ImGuizmo.IsUsing())
        {
            Model(_model, centerX, centerY, (float)angle, (float)_scale);
        }

        bool changed;
        fixed (float* viewMatrix = _view)
        fixed (float* projection = _projection)
        fixed (float* model = _model)
        {
            changed = ImGuizmo.Manipulate(viewMatrix, projection, _operation, ImGuizmoMode.Local, model);
        }

        ImGuiApi.End();
        _over = ImGuizmo.IsOver();
        _using = ImGuizmo.IsUsing();
        if (!changed)
        {
            return;
        }

        var x = _model[12] - (width / 2f) + view.Box.X;
        var y = _model[13] - (height / 2f) + view.Box.Y;
        if (Math.Abs(x - window.X) >= 1 || Math.Abs(y - window.Y) >= 1)
        {
            window.MoveTo((int)Math.Round(x), (int)Math.Round(y));
        }

        var turned = Math.Atan2(_model[1], _model[0]);
        if (double.IsNaN(_sentAngle) || Math.Abs(turned - _sentAngle) > 0.004)
        {
            if (Math.Abs(turned - angle) > 0.004)
            {
                _comp.RotateWindow(window, turned, relative: false);
            }

            _sentAngle = turned;
        }

        var scale = Math.Sqrt((_model[0] * _model[0]) + (_model[1] * _model[1]));
        if (Math.Abs(scale - _scale) > 0.005)
        {
            _scale = Math.Clamp(scale, 0.1, 8);
            _comp.SetInspectorScale(window, _scale);
        }
    }

    private static void Ortho(float[] into, float width, float height)
    {
        Array.Clear(into);
        into[0] = 2f / width;
        into[5] = -2f / height;
        into[10] = -1f;
        into[12] = -1f;
        into[13] = 1f;
        into[15] = 1f;
    }

    private static void Model(float[] into, float x, float y, float angle, float scale)
    {
        var cos = MathF.Cos(angle) * scale;
        var sin = MathF.Sin(angle) * scale;
        Array.Clear(into);
        into[0] = cos;
        into[1] = sin;
        into[4] = -sin;
        into[5] = cos;
        into[10] = scale;
        into[12] = x;
        into[13] = y;
        into[15] = 1;
    }
}
