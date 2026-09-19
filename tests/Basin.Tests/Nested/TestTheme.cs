using Basin.Frames.Metacity;

namespace Basin.Tests.Nested;

internal static class TestTheme
{
    public static MetacityTheme? Load(string name) =>
        name == "Atlanta" ? MetacityTheme.ParseFile(MetacityParserTests.Fixture("Atlanta", 1)) : null;
}
