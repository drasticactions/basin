using System.Runtime.InteropServices;
using Hexa.NET.ImGui;
using ImGuiApi = Hexa.NET.ImGui.ImGui;

namespace Basin.UI.ImGui;

internal sealed unsafe class ImGuiFonts : IDisposable
{
    private byte* _data;
    private ImFontAtlasPtr _atlas;
    private int _frame;

    internal ImGuiFonts(float size)
    {
        using (var stream = typeof(ImGuiFonts).Assembly.GetManifestResourceStream("NotoSansMono-Regular.ttf")
                            ?? throw new InvalidOperationException("Basin.UI.ImGui carries no NotoSansMono-Regular.ttf resource."))
        {
            Length = (int)stream.Length;
            _data = (byte*)NativeMemory.Alloc((nuint)Length);
            stream.ReadExactly(new Span<byte>(_data, Length));
        }

        Size = size;
        _atlas = ImGuiApi.ImFontAtlas();
        ImGuiApi.SetFontLoader(_atlas, ImGuiApi.GetFontLoader());
        var config = ImGuiApi.ImFontConfig();
        config.FontDataOwnedByAtlas = false;
        Font = ImGuiApi.AddFontFromMemoryTTF(_atlas, _data, Length, size, config);
        ImGuiApi.Destroy(config);
        if (Font.Handle is null)
        {
            Dispose();
            throw new InvalidOperationException("Dear ImGui would not load Noto Sans Mono.");
        }
    }

    internal ImFontAtlasPtr Atlas => _atlas;

    internal ImFontPtr Font { get; }

    internal float Size { get; }

    internal int Length { get; }

    internal void NextFrame() => ImGuiP.ImFontAtlasUpdateNewFrame(_atlas, ++_frame, rendererHasTextures: true);

    public void Dispose()
    {
        if (_atlas.Handle is not null)
        {
            ImGuiApi.Destroy(_atlas);
            _atlas = default;
        }

        if (_data is not null)
        {
            NativeMemory.Free(_data);
            _data = null;
        }
    }
}
