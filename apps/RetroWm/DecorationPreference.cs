using System.Text.RegularExpressions;
using Basin.WindowManager;
using Microsoft.Extensions.Logging;
using Tomlyn;
using Tomlyn.Model;

namespace RetroWm;

internal enum DecorationPreference
{
    ForceSsd,
    PreferSsd,
    Csd,
}
