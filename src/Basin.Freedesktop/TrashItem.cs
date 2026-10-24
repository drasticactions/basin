namespace Basin.Freedesktop;

/// <summary>
/// One item in the home trash, as <see cref="FreedesktopTrash"/> lists or creates it.
/// </summary>
/// <param name="Name">The unique name of the item in the trash, which can differ from the original file name.</param>
/// <param name="OriginalPath">The absolute path that the item had before it went to the trash.</param>
/// <param name="DeletedAt">The local deletion time from the <c>.trashinfo</c> file.</param>
/// <param name="FilesPath">The path of the file or directory under the trash <c>files</c> directory.</param>
/// <param name="InfoPath">The path of the <c>.trashinfo</c> file.</param>
public sealed record TrashItem(string Name, string OriginalPath, DateTime DeletedAt, string FilesPath, string InfoPath)
{
    /// <summary>
    /// Gets a value indicating whether <see cref="FilesPath"/> is a directory now. Each read asks the file system.
    /// </summary>
    public bool IsDirectory => Directory.Exists(FilesPath);
}
