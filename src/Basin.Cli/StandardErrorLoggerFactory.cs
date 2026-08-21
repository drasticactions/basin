using Basin.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Basin.Cli;

internal sealed class StandardErrorLoggerFactory(LogLevel minimum) : ILoggerFactory
{
    public ILogger CreateLogger(string categoryName) => new StandardErrorLogger(categoryName, minimum);

    public void AddProvider(ILoggerProvider provider) =>
        throw new NotSupportedException("this factory writes to standard error only");

    public void Dispose()
    {
    }
}
