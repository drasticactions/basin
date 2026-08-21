using Xunit;

namespace Basin.Tests;

public sealed class AllocationBudgetTests
{
    private static readonly string[] Kinds = ["exact", "ceiling"];

    private static readonly string[] Scopes =
    [
        "server", "client", "channel", "native", "workload", "zerogc",
        "server-managed", "client-managed",
    ];

    [Fact]
    public void Every_row_names_a_scope_and_a_kind_the_table_knows()
    {
        foreach (var row in Budgets.Rows())
        {
            Assert.Contains(row.Kind, Kinds);
            Assert.Contains(row.Scope, Scopes);
            Assert.NotEmpty(row.Path);
            Assert.True(row.Bytes >= 0, $"'{row.Scope} {row.Path}' carries a negative budget.");
        }
    }

    [Fact]
    public void No_two_rows_name_the_same_path_in_the_same_scope()
    {
        var seen = new HashSet<(string Scope, string Path)>();
        foreach (var row in Budgets.Rows())
        {
            Assert.True(seen.Add((row.Scope, row.Path)), $"'{row.Scope} {row.Path}' appears twice.");
        }
    }

    [Fact]
    public void The_table_is_not_empty()
    {
        Assert.NotEmpty(Budgets.Rows());
    }
}
