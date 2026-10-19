using Hexa.NET.ImGui;
using ImGuiApi = Hexa.NET.ImGui.ImGui;

namespace Basin.UI.ImGui;

internal static unsafe class ImGuiTextures
{
    internal static int IndexOf(ImTextureID id) => (int)id.Handle - 1;

    internal static ImTextureID IdOf(int index) => new((ulong)(index + 1));

    internal static int Resolve(in ImTextureRef reference) =>
        IndexOf(reference.TexData is not null ? reference.TexData->TexID : reference.TexID);

    internal static bool Process(ImDrawData* data, IImGuiBackend backend)
    {
        var textures = data->Textures;
        if (textures is null)
        {
            return false;
        }

        var changed = false;
        for (var i = 0; i < textures->Size; i++)
        {
            var texture = textures->Data[i].Handle;
            if (texture is null || texture->Status == ImTextureStatus.Ok)
            {
                continue;
            }

            changed |= Apply(texture, backend);
        }

        return changed;
    }

    internal static void DestroyAll(ImDrawData* data, IImGuiBackend backend)
    {
        var textures = data->Textures;
        if (textures is null)
        {
            return;
        }

        for (var i = 0; i < textures->Size; i++)
        {
            var texture = textures->Data[i].Handle;
            if (texture is not null && !texture->TexID.IsNull)
            {
                backend.DestroyTexture(IndexOf(texture->TexID));
                ImGuiApi.SetTexID(texture, ImTextureID.Null);
                ImGuiApi.SetStatus(texture, ImTextureStatus.Destroyed);
            }
        }
    }

    private static bool Apply(ImTextureData* texture, IImGuiBackend backend)
    {
        switch (texture->Status)
        {
            case ImTextureStatus.WantCreate:
            {
                if (texture->BytesPerPixel != 4 || texture->Pixels is null)
                {
                    return false;
                }

                var index = backend.CreateTexture(texture->Width, texture->Height);
                if (index < 0)
                {
                    return false;
                }

                backend.UpdateTexture(
                    index, 0, 0, texture->Width, texture->Height, texture->Pixels, texture->Width * 4);
                ImGuiApi.SetTexID(texture, IdOf(index));
                ImGuiApi.SetStatus(texture, ImTextureStatus.Ok);
                return true;
            }

            case ImTextureStatus.WantUpdates:
            {
                var index = IndexOf(texture->TexID);
                if (index < 0 || texture->Pixels is null)
                {
                    return false;
                }

                var pitch = texture->Width * 4;
                var updates = texture->Updates;
                if (updates.Size == 0)
                {
                    Upload(texture, backend, index, pitch, texture->UpdateRect);
                }

                for (var i = 0; i < updates.Size; i++)
                {
                    Upload(texture, backend, index, pitch, updates.Data[i]);
                }

                ImGuiApi.SetStatus(texture, ImTextureStatus.Ok);
                return true;
            }

            case ImTextureStatus.WantDestroy when texture->UnusedFrames > backend.FramesInFlight:
            {
                var index = IndexOf(texture->TexID);
                if (index >= 0)
                {
                    backend.DestroyTexture(index);
                }

                ImGuiApi.SetTexID(texture, ImTextureID.Null);
                ImGuiApi.SetStatus(texture, ImTextureStatus.Destroyed);
                return true;
            }

            default:
                return false;
        }
    }

    private static void Upload(ImTextureData* texture, IImGuiBackend backend, int index, int pitch, ImTextureRect rect)
    {
        if (rect.W == 0 || rect.H == 0)
        {
            return;
        }

        var pixels = texture->Pixels + (rect.Y * pitch) + (rect.X * 4);
        backend.UpdateTexture(index, rect.X, rect.Y, rect.W, rect.H, pixels, pitch);
    }
}
