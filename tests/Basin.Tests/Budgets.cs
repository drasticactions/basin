using System.Globalization;
using System.Runtime.CompilerServices;
using Basin.Diagnostics;
using Xunit;

namespace Basin.Tests;

internal readonly record struct BudgetRow(string Scope, string Kind, string Path, long Bytes);

internal static class Budgets
{
    private const string UpdateVariable = "BASIN_UPDATE_BUDGETS";

    private const string Reason =
        "allocation budgets are recorded in Release with -p:BasinCounters=true; this build measures different numbers";

    public static bool Recorded =>
#if DEBUG
        false;
#else
        BasinCounters.Enabled;
#endif

    private const int VirtualCallCacheMaximum = 8 * 1024;

    private const int CastCacheMaximumInts = (4096 + 1) * 6;

    private static bool _settled;

    public static void Require()
    {
        Assert.SkipWhen(!Recorded, Reason);
        SettleRuntimeCaches();
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Test-only reach into CoreLib; absent under NativeAOT, where it returns early.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2057", Justification = "Test-only reach into CoreLib; absent under NativeAOT, where it returns early.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Test-only reach into CoreLib; absent under NativeAOT, where it returns early.")]
    internal static void SettleRuntimeCaches()
    {
        if (_settled)
        {
            return;
        }

        _settled = true;
        const System.Reflection.BindingFlags Private = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance;
        var helpers = typeof(object).Assembly.GetType("System.Runtime.CompilerServices.VirtualDispatchHelpers");
        if (helpers?.GetField("s_virtualFunctionPointerCache", Private) is not { } field ||
            field.GetValue(null) is not { } cache ||
            cache.GetType().GetMethod("MaybeReplaceCacheWithLarger", Private) is not { } grow)
        {
            return;
        }

        grow.Invoke(cache, [VirtualCallCacheMaximum]);
        field.SetValue(null, cache);
        FillCastCache();
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "Test-only reach into CoreLib; absent under NativeAOT, where it returns early.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2057", Justification = "Test-only reach into CoreLib; absent under NativeAOT, where it returns early.")]
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "Test-only reach into CoreLib; absent under NativeAOT, where it returns early.")]
    private static void FillCastCache()
    {
        const System.Reflection.BindingFlags Private = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
        var table = typeof(object).Assembly.GetType("System.Runtime.CompilerServices.CastHelpers")?.GetField("s_table", Private);
        if (table is null)
        {
            return;
        }

        int Length() => (table.GetValue(null) as Array)?.Length ?? 0;
        var types = new List<Type>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                types.AddRange(assembly.GetTypes().Where(type => !type.ContainsGenericParameters && !type.IsByRefLike && !type.IsPointer));
            }
            catch (System.Reflection.ReflectionTypeLoadException error)
            {
                types.AddRange(error.Types.OfType<Type>().Where(type => !type.ContainsGenericParameters && !type.IsByRefLike && !type.IsPointer));
            }
        }

        var targets = types.Where(type => type.IsInterface).Take(64).ToArray();
        var full = CastCacheMaximumInts;
        for (var i = 0; i < types.Count && Length() < full; i++)
        {
            foreach (var target in targets)
            {
                _ = target.IsAssignableFrom(types[i]);
            }
        }
    }

    public static bool Updating => Environment.GetEnvironmentVariable(UpdateVariable) == "1";

    public static IReadOnlyList<BudgetRow> Rows([CallerFilePath] string sourcePath = "") =>
        Read(PathOf(sourcePath));

    public static void Check(string scope, string path, long measured, [CallerFilePath] string sourcePath = "")
    {
        if ((scope is "server" or "client") &&
            CompositorTestHost.TransportUnderTest == Basin.Cli.TransportKind.Managed)
        {
            scope += "-managed";
        }

        var file = PathOf(sourcePath);
        if (Updating)
        {
            Rewrite(file, scope, path, measured);
            return;
        }

        BudgetRow? match = null;
        foreach (var candidate in Read(file))
        {
            if (candidate.Scope == scope && candidate.Path == path)
            {
                match = candidate;
                break;
            }
        }

        if (match is not { } row)
        {
            throw new InvalidOperationException(
                $"No budget row for '{scope} {path}'. Run once with {UpdateVariable}=1 and commit {file}.");
        }

        switch (row.Kind)
        {
            case "exact":
                Assert.Equal(row.Bytes, measured);
                break;
            case "ceiling":
                Assert.True(
                    measured <= row.Bytes,
                    $"'{scope} {path}' allocated {measured} bytes against a ceiling of {row.Bytes}.");
                break;
            default:
                throw new InvalidOperationException($"'{scope} {path}' has an unknown kind '{row.Kind}'.");
        }
    }

    private static string PathOf(string sourcePath) =>
        System.IO.Path.Combine(System.IO.Path.GetDirectoryName(sourcePath)!, "Budgets", "allocation.txt");

    private static List<BudgetRow> Read(string file)
    {
        var rows = new List<BudgetRow>();
        foreach (var line in File.ReadLines(file))
        {
            if (Parse(line) is { } row)
            {
                rows.Add(row);
            }
        }

        return rows;
    }

    private static BudgetRow? Parse(string line)
    {
        var text = line.Trim();
        if (text.Length == 0 || text[0] == '#')
        {
            return null;
        }

        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4)
        {
            throw new InvalidOperationException($"A budget row needs four columns: '{line}'.");
        }

        return new BudgetRow(parts[0], parts[1], parts[2], long.Parse(parts[3], CultureInfo.InvariantCulture));
    }

    private static void Rewrite(string file, string scope, string path, long measured)
    {
        var lines = File.ReadAllLines(file);
        var found = false;

        for (var i = 0; i < lines.Length; i++)
        {
            if (Parse(lines[i]) is not { } row || row.Scope != scope || row.Path != path)
            {
                continue;
            }

            lines[i] = Format(row with { Bytes = measured });
            found = true;
            break;
        }

        File.WriteAllLines(
            file,
            found ? lines : [.. lines, Format(new BudgetRow(scope, "exact", path, measured))]);
    }

    private static string Format(BudgetRow row) => string.Create(
        CultureInfo.InvariantCulture,
        $"{row.Scope,-10} {row.Kind,-8} {row.Path,-28} {row.Bytes,6}");
}
