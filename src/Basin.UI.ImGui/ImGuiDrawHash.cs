using Hexa.NET.ImGui;

namespace Basin.UI.ImGui;

internal static unsafe class ImGuiDrawHash
{
    internal static long Of(ImDrawData* data)
    {
        var hash = new HashCode();
        hash.Add(data->DisplaySize.X);
        hash.Add(data->DisplaySize.Y);
        hash.Add(data->FramebufferScale.X);
        var lists = data->CmdLists;
        for (var i = 0; i < lists.Size; i++)
        {
            var list = lists.Data[i].Handle;
            if (list is null)
            {
                continue;
            }

            hash.AddBytes(new ReadOnlySpan<byte>(list->VtxBuffer.Data, list->VtxBuffer.Size * sizeof(ImDrawVert)));
            hash.AddBytes(new ReadOnlySpan<byte>(list->IdxBuffer.Data, list->IdxBuffer.Size * sizeof(ushort)));
            for (var c = 0; c < list->CmdBuffer.Size; c++)
            {
                ref var command = ref list->CmdBuffer.Data[c];
                hash.Add(command.ClipRect);
                hash.Add(ImGuiTextures.Resolve(command.TexRef));
                hash.Add(command.VtxOffset);
                hash.Add(command.IdxOffset);
                hash.Add(command.ElemCount);
                hash.Add((nint)command.UserCallback);
            }
        }

        var low = hash.ToHashCode();
        return ((long)lists.Size << 32) ^ (uint)low ^ ((long)data->TotalVtxCount << 40);
    }
}
