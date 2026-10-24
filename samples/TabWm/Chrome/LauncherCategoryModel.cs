namespace TabWm.Chrome;

public sealed record LauncherCategoryModel(string Name, IReadOnlyList<LauncherEntryModel> Items);
