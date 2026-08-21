using System.Globalization;
using Microsoft.Extensions.Logging;
using Tomlyn;
using Tomlyn.Model;

namespace EightWm;

internal sealed record Rule(string AppId, int MinWidth);
