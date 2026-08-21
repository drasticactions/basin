using Basin.WindowManager;
using RetroWm.Protocol;
using SkiaSharp;
using Wayland;

namespace RetroWm;

internal readonly record struct DockEntry(ManagedWindow Window, string Title, SKImage? Icon);
