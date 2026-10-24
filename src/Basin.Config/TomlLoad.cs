using Tomlyn.Model;

namespace Basin.Config;

/// <summary>
/// The result of <see cref="TomlConfig.Load"/>.
/// </summary>
/// <param name="Table">The parsed table, or <see langword="null"/> when no file was read.</param>
/// <param name="File">The path that was tried, or <see langword="null"/> when loading was turned off.</param>
/// <param name="Fatal">An error for a file that the user named and that did not load, or <see langword="null"/>. The caller must stop when it is set.</param>
public readonly record struct TomlLoad(TomlTable? Table, string? File, string? Fatal);
