using Microsoft.Extensions.Logging;
using Tomlyn;
using Tomlyn.Model;

namespace Waylonia;

internal sealed record HostProfile(string Ssh, string? Command, string? Compress);
