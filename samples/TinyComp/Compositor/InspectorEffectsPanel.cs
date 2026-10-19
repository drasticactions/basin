using System.Numerics;
using Basin.Inspector;
using Hexa.NET.ImGui;
using Hexa.NET.ImNodes;
using ImGuiApi = Hexa.NET.ImGui.ImGui;

namespace TinyComp;

internal sealed unsafe class InspectorEffectsPanel : IInspectorPanel
{
    private static readonly string[] Flags =
        ["wobbly", "switcher", "stretch", "slide_back", "notifications", "dim_inactive", "drop_shadow", "highlight"];

    private static readonly string[] Animations = ["open", "close", "minimize"];

    private readonly TinyComp _comp;
    private readonly List<string> _chain = [];
    private string _placed = string.Empty;

    internal InspectorEffectsPanel(TinyComp comp)
    {
        _comp = comp;
    }

    public ReadOnlySpan<byte> Name => "Effects"u8;

    public string? LastError { get; private set; }

    public void Draw(ref InspectorDraw draw)
    {
        var text = draw.Text;
        var config = _comp.CurrentConfig;
        ImGuiApi.SeparatorText("window effects"u8);
        for (var i = 0; i < Flags.Length; i++)
        {
            var on = FlagOf(config, Flags[i]);
            if (ImGuiApi.Checkbox(text.Encode(Flags[i]), ref on))
            {
                Apply($"effects.{Flags[i]}", on ? "true" : "false");
            }

            if (i % 4 != 3)
            {
                ImGuiApi.SameLine();
            }
        }

        foreach (var animation in Animations)
        {
            var current = animation switch
            {
                "open" => config.OpenAnimation,
                "close" => config.CloseAnimation,
                _ => config.MinimizeAnimation,
            } ?? "none";
            ImGuiApi.SetNextItemWidth(160);
            if (ImGuiApi.BeginCombo(text.Encode(animation), text.Encode(current)))
            {
                foreach (var choice in SettingsCatalog.Find($"effects.{animation}")?.Choices ?? [])
                {
                    if (ImGuiApi.Selectable(text.Encode(choice), choice == current))
                    {
                        Apply($"effects.{animation}", $"\"{choice}\"");
                    }
                }

                ImGuiApi.EndCombo();
            }
        }

        ImGuiApi.SeparatorText("post stages"u8);
        _chain.Clear();
        _chain.AddRange(config.Post);
        if (ImGuiApi.BeginCombo("add a stage"u8, "..."u8))
        {
            foreach (var stage in SettingsCatalog.PostStages)
            {
                if (!_chain.Contains(stage) && ImGuiApi.Selectable(text.Encode(stage)))
                {
                    _chain.Add(stage);
                    ApplyChain();
                }
            }

            ImGuiApi.EndCombo();
        }

        if (LastError is { } error)
        {
            ImGuiApi.TextColored(new Vector4(1f, 0.45f, 0.4f, 1f), text.Encode(error));
        }

        Graph(text);
    }

    private void Graph(InspectorText text)
    {
        ImNodes.BeginNodeEditor();
        var signature = string.Join('>', _chain);
        if (signature != _placed)
        {
            _placed = signature;
            ImNodes.SetNodeGridSpacePos(0, new Vector2(10, 40));
            for (var i = 0; i < _chain.Count; i++)
            {
                ImNodes.SetNodeGridSpacePos(i + 1, new Vector2(150 + (i * 170), 40 + ((i % 2) * 30)));
            }

            ImNodes.SetNodeGridSpacePos(_chain.Count + 1, new Vector2(150 + (_chain.Count * 170), 40));
        }

        ImNodes.BeginNode(0);
        ImNodes.BeginNodeTitleBar();
        ImGuiApi.TextUnformatted("scene"u8);
        ImNodes.EndNodeTitleBar();
        ImNodes.BeginOutputAttribute(2);
        ImGuiApi.TextUnformatted("frame"u8);
        ImNodes.EndOutputAttribute();
        ImNodes.EndNode();

        string? remove = null;
        var moveLeft = -1;
        for (var i = 0; i < _chain.Count; i++)
        {
            var id = i + 1;
            ImNodes.BeginNode(id);
            ImNodes.BeginNodeTitleBar();
            ImGuiApi.TextUnformatted(text.Encode(_chain[i]));
            ImNodes.EndNodeTitleBar();
            ImNodes.BeginInputAttribute((id * 2) + 1);
            ImGuiApi.PushID(id);
            if (i > 0)
            {
                if (ImGuiApi.SmallButton("<"u8))
                {
                    moveLeft = i;
                }

                ImGuiApi.SameLine();
            }

            if (ImGuiApi.SmallButton("remove"u8))
            {
                remove = _chain[i];
            }

            ImGuiApi.PopID();
            ImNodes.EndInputAttribute();
            ImNodes.BeginOutputAttribute((id * 2) + 2);
            ImGuiApi.TextUnformatted("out"u8);
            ImNodes.EndOutputAttribute();
            ImNodes.EndNode();
        }

        var sink = _chain.Count + 1;
        ImNodes.BeginNode(sink);
        ImNodes.BeginNodeTitleBar();
        ImGuiApi.TextUnformatted("output"u8);
        ImNodes.EndNodeTitleBar();
        ImNodes.BeginInputAttribute((sink * 2) + 1);
        ImGuiApi.TextUnformatted("present"u8);
        ImNodes.EndInputAttribute();
        ImNodes.EndNode();

        for (var i = 0; i <= _chain.Count; i++)
        {
            ImNodes.Link(i, (i * 2) + 2, ((i + 1) * 2) + 1);
        }

        ImNodes.EndNodeEditor();

        if (remove is not null)
        {
            _chain.Remove(remove);
            ApplyChain();
        }
        else if (moveLeft > 0)
        {
            (_chain[moveLeft - 1], _chain[moveLeft]) = (_chain[moveLeft], _chain[moveLeft - 1]);
            ApplyChain();
        }
    }

    private void ApplyChain() =>
        Apply("effects.post", _chain.Count == 0 ? "\"none\"" : $"[{string.Join(", ", _chain.Select(stage => $"\"{stage}\""))}]");

    private void Apply(string key, string literal) => LastError = _comp.ApplyLiveSetting(key, literal);

    private static bool FlagOf(Config config, string name) => name switch
    {
        "wobbly" => config.Wobbly,
        "switcher" => config.Switcher,
        "stretch" => config.Stretch,
        "slide_back" => config.SlideBack,
        "notifications" => config.Notifications,
        "dim_inactive" => config.DimInactive,
        "drop_shadow" => config.DropShadow,
        _ => config.Highlight,
    };
}
