using Basin.WindowManager;
using Basin.WindowManager.Skia;
using Basin.WindowManager.Skia.Protocol;
using SkiaSharp;
using Wayland;

namespace RetroWm;

internal readonly record struct DockEntry(ManagedWindow Window, string Title, SKImage? Icon);
