using Basin.Freedesktop;
using Xunit;

namespace Basin.Tests;

public sealed class FreedesktopTrashTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("basin-trash-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Home(string name) => Path.Combine(_root, "home", name);

    [Fact]
    public void Trashing_writes_a_trashinfo_and_moves_the_file()
    {
        var trash = new FreedesktopTrash(Path.Combine(_root, "Trash"));
        Directory.CreateDirectory(Path.Combine(_root, "home"));
        File.WriteAllText(Home("my notes.txt"), "hello");

        var item = trash.Trash(Home("my notes.txt"), new DateTime(2026, 9, 29, 13, 5, 7));

        Assert.False(File.Exists(Home("my notes.txt")));
        Assert.True(File.Exists(Path.Combine(trash.FilesDirectory, "my notes.txt")));
        var info = File.ReadAllText(Path.Combine(trash.InfoDirectory, "my notes.txt.trashinfo"));
        Assert.Contains("[Trash Info]", info);
        Assert.Contains("Path=" + FreedesktopTrash.Encode(Home("my notes.txt")), info);
        Assert.Contains("%20", info);
        Assert.Contains("DeletionDate=2026-09-29T13:05:07", info);
        Assert.Equal(Home("my notes.txt"), trash.Items().Single().OriginalPath);
        Assert.Equal(item.Name, trash.Items().Single().Name);
        Assert.False(trash.IsEmpty);
    }

    [Fact]
    public void A_second_item_with_the_same_name_gets_a_new_one()
    {
        var trash = new FreedesktopTrash(Path.Combine(_root, "Trash"));
        Directory.CreateDirectory(Path.Combine(_root, "home"));
        File.WriteAllText(Home("a.txt"), "1");
        var first = trash.Trash(Home("a.txt"));
        File.WriteAllText(Home("a.txt"), "22");
        var second = trash.Trash(Home("a.txt"));

        Assert.Equal("a.txt", first.Name);
        Assert.Equal("a.2.txt", second.Name);
        Assert.Equal(new TrashSummary(2, 3), trash.Summary());
    }

    [Fact]
    public void Directories_are_sized_through_directorysizes_and_restored_in_place()
    {
        var trash = new FreedesktopTrash(Path.Combine(_root, "Trash"));
        Directory.CreateDirectory(Home("Project"));
        File.WriteAllBytes(Path.Combine(Home("Project"), "data.bin"), new byte[1000]);

        var item = trash.Trash(Home("Project"));
        Assert.True(item.IsDirectory);
        Assert.Contains($"1000 ", File.ReadAllText(Path.Combine(trash.Root, "directorysizes")));
        Assert.Equal(new TrashSummary(1, 1000), trash.Summary());

        trash.Restore(trash.Items().Single());
        Assert.True(File.Exists(Path.Combine(Home("Project"), "data.bin")));
        Assert.True(trash.IsEmpty);
        Assert.DoesNotContain("Project", File.ReadAllText(Path.Combine(trash.Root, "directorysizes")));
    }

    [Fact]
    public void Restoring_over_an_existing_file_is_refused()
    {
        var trash = new FreedesktopTrash(Path.Combine(_root, "Trash"));
        Directory.CreateDirectory(Path.Combine(_root, "home"));
        File.WriteAllText(Home("b.txt"), "old");
        trash.Trash(Home("b.txt"));
        File.WriteAllText(Home("b.txt"), "new");

        Assert.Throws<IOException>(() => trash.Restore(trash.Items().Single()));
        Assert.False(trash.IsEmpty);
    }

    [Fact]
    public void Emptying_removes_every_item_and_the_size_cache()
    {
        var trash = new FreedesktopTrash(Path.Combine(_root, "Trash"));
        Directory.CreateDirectory(Home("dir"));
        File.WriteAllText(Home("c.txt"), "c");
        trash.Trash(Home("c.txt"));
        trash.Trash(Home("dir"));

        trash.Empty();
        Assert.True(trash.IsEmpty);
        Assert.Empty(Directory.EnumerateFileSystemEntries(trash.FilesDirectory));
        Assert.False(File.Exists(Path.Combine(trash.Root, "directorysizes")));
    }

    [Fact]
    public void An_info_file_without_its_file_is_not_an_item()
    {
        var trash = new FreedesktopTrash(Path.Combine(_root, "Trash"));
        Directory.CreateDirectory(trash.InfoDirectory);
        File.WriteAllText(Path.Combine(trash.InfoDirectory, "ghost.trashinfo"), "[Trash Info]\nPath=/tmp/ghost\nDeletionDate=2026-01-01T00:00:00\n");
        Assert.Empty(trash.Items());
        Assert.True(trash.IsEmpty);
    }

    [Fact]
    public void Empty_removes_the_orphans_that_items_skips()
    {
        var trash = new FreedesktopTrash(Path.Combine(_root, "Trash"));
        Directory.CreateDirectory(Path.Combine(_root, "home"));
        File.WriteAllText(Home("kept.txt"), "kept");
        trash.Trash(Home("kept.txt"));
        File.WriteAllText(Path.Combine(trash.InfoDirectory, "ghost.trashinfo"), "[Trash Info]\nPath=/tmp/ghost\nDeletionDate=2026-01-01T00:00:00\n");
        File.WriteAllText(Path.Combine(trash.FilesDirectory, "stray.txt"), "no info");
        Directory.CreateDirectory(Path.Combine(trash.FilesDirectory, "stray-dir", "inner"));
        Assert.Single(trash.Items());
        Assert.False(trash.IsEmpty);

        trash.Empty();
        Assert.Empty(Directory.EnumerateFileSystemEntries(trash.FilesDirectory));
        Assert.Empty(Directory.EnumerateFileSystemEntries(trash.InfoDirectory));
        Assert.True(trash.IsEmpty);
    }
}
