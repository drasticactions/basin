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

    public static void Require() => Assert.SkipWhen(!Recorded, Reason);

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
