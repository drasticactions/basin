using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using SDL3;
using Xunit;

namespace SdlComp.Tests;

public sealed class SdlExportTests
{
    private static readonly string[] Callers = ["Basin.Sdl", "Basin.Sdl.View", "sdl-comp"];

    [Fact]
    public void Every_SDL_function_basin_calls_is_exported_by_the_packaged_library()
    {
        var entryPoints = EntryPointsByWrapperName();
        var called = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var caller in Callers)
        {
            called.UnionWith(ReferencedSdlMethods(Path.Combine(AppContext.BaseDirectory, caller + ".dll")));
        }

        Assert.NotEmpty(called);
        var library = NativeLibrary.Load("SDL3", typeof(SDL).Assembly, DllImportSearchPath.AssemblyDirectory | DllImportSearchPath.SafeDirectories);
        try
        {
            var missing = new List<string>();
            foreach (var name in called)
            {
                if (!entryPoints.TryGetValue(name, out var entries))
                {
                    missing.Add($"{name} (no entry point in the binding)");
                    continue;
                }

                if (!entries.Any(entry => NativeLibrary.TryGetExport(library, entry, out _)))
                {
                    missing.Add($"{name} ({string.Join(", ", entries)})");
                }
            }

            Assert.True(missing.Count == 0, $"not exported by the packaged libSDL3: {string.Join("; ", missing)}");
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    private static Dictionary<string, List<string>> EntryPointsByWrapperName()
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "SDL3-CS.dll"));
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        foreach (var handle in reader.MethodDefinitions)
        {
            var method = reader.GetMethodDefinition(handle);
            var import = method.GetImport();
            if (import.Module.IsNil)
            {
                continue;
            }

            var entry = import.Name.IsNil ? reader.GetString(method.Name) : reader.GetString(import.Name);
            if (!entry.StartsWith("SDL_", StringComparison.Ordinal))
            {
                continue;
            }

            var wrapper = entry["SDL_".Length..].Replace("_", string.Empty, StringComparison.Ordinal);
            if (!map.TryGetValue(wrapper, out var list))
            {
                map[wrapper] = list = [];
            }

            if (!list.Contains(entry))
            {
                list.Add(entry);
            }
        }

        return map;
    }

    private static IEnumerable<string> ReferencedSdlMethods(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        var names = new List<string>();
        foreach (var handle in reader.MemberReferences)
        {
            var member = reader.GetMemberReference(handle);
            if (member.GetKind() != MemberReferenceKind.Method || member.Parent.Kind != HandleKind.TypeReference)
            {
                continue;
            }

            var parent = reader.GetTypeReference((TypeReferenceHandle)member.Parent);
            if (reader.GetString(parent.Name) == "SDL" && reader.GetString(parent.Namespace) == "SDL3")
            {
                names.Add(reader.GetString(member.Name));
            }
        }

        return names;
    }
}
