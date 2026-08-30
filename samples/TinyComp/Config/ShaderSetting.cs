namespace TinyComp;

internal sealed record ShaderSetting(string Path, IReadOnlyDictionary<string, double> Parameters);
