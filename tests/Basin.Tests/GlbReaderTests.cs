using System.Buffers.Binary;
using System.Text;
using Basin.Scene3D.Gltf;
using Xunit;

namespace Basin.Tests;

public sealed class GlbReaderTests
{
    [Fact]
    public void A_triangle_flattens_into_one_mesh()
    {
        var document = GlbDocument.Parse(Glb(mode: 4, extensions: null));
        var mesh = GlbMeshes.Flatten(document, [], null);
        Assert.Equal(3, mesh.Vertices.Length);
        Assert.Equal([0u, 1u, 2u], mesh.Indices);
    }

    [Fact]
    public void Lines_are_refused_by_name()
    {
        var document = GlbDocument.Parse(Glb(mode: 1, extensions: null));
        var error = Assert.Throws<NotSupportedException>(() => GlbMeshes.Flatten(document, [], null));
        Assert.Contains("Only triangles", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_required_extension_is_refused_by_name()
    {
        var error = Assert.Throws<NotSupportedException>(() => GlbDocument.Parse(Glb(mode: 4, extensions: "KHR_draco_mesh_compression")));
        Assert.Contains("KHR_draco_mesh_compression", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_cubic_spline_channel_reads_the_values_between_the_tangents()
    {
        var model = GlbAvatar.Load(AnimatedGlb("CUBICSPLINE"), 4, []);
        var clip = model.Clips["move"];
        Assert.Equal([0], clip.Nodes);
        Assert.Equal(5, clip.FrameCount);
        Assert.Equal(0f, clip.Translations[0].X, 5);
        Assert.Equal(0.3125f, clip.Translations[1].X, 5);
        Assert.Equal(1f, clip.Translations[2].X, 5);
        Assert.Equal(1.6875f, clip.Translations[3].X, 5);
        Assert.Equal(2f, clip.Translations[4].X, 5);
    }

    [Fact]
    public void An_unknown_interpolation_is_refused_by_name()
    {
        var error = Assert.Throws<NotSupportedException>(() => GlbAvatar.Load(AnimatedGlb("BEZIER"), 4, []));
        Assert.Contains("BEZIER", error.Message, StringComparison.Ordinal);
    }

    private static byte[] AnimatedGlb(string interpolation)
    {
        float[] floats =
        [
            0, 0, 0, 1, 0, 0, 0, 1, 0,
            0, 1,
            0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 2, 0, 0, 0, 0, 0,
        ];
        var binary = new byte[floats.Length * 4];
        for (var i = 0; i < floats.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(binary.AsSpan(i * 4), floats[i]);
        }

        var json = "{\"asset\":{\"version\":\"2.0\"},\"scene\":0,\"scenes\":[{\"nodes\":[0,1]}]," +
            "\"nodes\":[{\"name\":\"root\"},{\"name\":\"prop\",\"mesh\":0}],\"skins\":[{\"joints\":[0]}]," +
            "\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0},\"mode\":4}]}]," +
            "\"animations\":[{\"name\":\"move\",\"channels\":[{\"sampler\":0,\"target\":{\"node\":0,\"path\":\"translation\"}}]," +
            $"\"samplers\":[{{\"input\":1,\"output\":2,\"interpolation\":\"{interpolation}\"}}]}}]," +
            "\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\"}," +
            "{\"bufferView\":1,\"componentType\":5126,\"count\":2,\"type\":\"SCALAR\"}," +
            "{\"bufferView\":2,\"componentType\":5126,\"count\":6,\"type\":\"VEC3\"}]," +
            "\"bufferViews\":[{\"buffer\":0,\"byteOffset\":0,\"byteLength\":36},{\"buffer\":0,\"byteOffset\":36,\"byteLength\":8}," +
            $"{{\"buffer\":0,\"byteOffset\":44,\"byteLength\":72}}],\"buffers\":[{{\"byteLength\":{binary.Length}}}]}}";
        return Pack(json, binary);
    }

    private static byte[] Glb(int mode, string? extensions)
    {
        var binary = new byte[36];
        float[] positions = [0, 0, 0, 1, 0, 0, 0, 1, 0];
        for (var i = 0; i < positions.Length; i++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(binary.AsSpan(i * 4), positions[i]);
        }

        var required = extensions is null ? string.Empty : $",\"extensionsRequired\":[\"{extensions}\"]";
        var json = "{\"asset\":{\"version\":\"2.0\"},\"scene\":0,\"scenes\":[{\"nodes\":[0]}],\"nodes\":[{\"mesh\":0}]," +
            $"\"meshes\":[{{\"primitives\":[{{\"attributes\":{{\"POSITION\":0}},\"mode\":{mode}}}]}}]," +
            "\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\"}]," +
            "\"bufferViews\":[{\"buffer\":0,\"byteOffset\":0,\"byteLength\":36}],\"buffers\":[{\"byteLength\":36}]" +
            required + "}";
        return Pack(json, binary);
    }

    private static byte[] Pack(string json, byte[] binary)
    {
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        var jsonLength = (jsonBytes.Length + 3) & ~3;
        var total = 12 + 8 + jsonLength + 8 + binary.Length;
        var glb = new byte[total];
        BinaryPrimitives.WriteUInt32LittleEndian(glb, 0x46546C67);
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(8), (uint)total);
        BinaryPrimitives.WriteInt32LittleEndian(glb.AsSpan(12), jsonLength);
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(16), 0x4E4F534A);
        glb.AsSpan(20, jsonLength).Fill((byte)' ');
        jsonBytes.CopyTo(glb, 20);
        var binaryAt = 20 + jsonLength;
        BinaryPrimitives.WriteInt32LittleEndian(glb.AsSpan(binaryAt), binary.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(binaryAt + 4), 0x004E4942);
        binary.CopyTo(glb, binaryAt + 8);
        return glb;
    }
}
