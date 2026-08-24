using Basin;
using System.Runtime.InteropServices;
using Basin.Cli;
using Microsoft.Extensions.Logging;
using Wayland;
using WorkspacePager.Protocol;

namespace WorkspacePager;

internal static class Program
{
    private const int Margin = 10;
    private const int Gap = 8;
    private const int CellWidth = 240;
    private const int DragThreshold = 4;
    private const string CreatedName = "pager";

    private sealed class OutputInfo
    {
        public required WlOutput Proxy { get; init; }

        public uint GlobalName;
        public PagerSurface? Pager;
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public int Scale = 1;

        public int LogicalWidth => Math.Max(1, Width / Math.Max(1, Scale));

        public int LogicalHeight => Math.Max(1, Height / Math.Max(1, Scale));
    }

    private sealed class WorkspaceEntry
    {
        public required ExtWorkspaceHandleV1 Handle { get; init; }

        public string? Id;
        public string Name = "";
        public uint State;
        public uint? Position;
        public GroupEntry? Group;
    }

    private sealed class GroupEntry
    {
        public required ExtWorkspaceGroupHandleV1 Handle { get; init; }

        public bool CanCreate;
        public OutputInfo? Output;
        public readonly List<WorkspaceEntry> Members = [];
    }

    private sealed class PlasmaWin
    {
        public required string Uuid { get; init; }

        public required OrgKdePlasmaWindow Window { get; init; }

        public int X;
        public int Y;
        public int Width;
        public int Height;
        public bool Active;
        public readonly HashSet<string> Desktops = [];
    }

    private sealed class PagerSurface
    {
        public required WlSurface Surface { get; init; }

        public required ZwlrLayerSurfaceV1 Layer { get; init; }

        public bool Configured;
        public ShmBuffer? Shown;
    }

    private readonly record struct CellHit(GroupEntry Group, WorkspaceEntry? Workspace, bool IsCreate, int Left, int Top, int Width, int Height);

    private static int Main(string[] args)
    {
        var cli = new BasinCommand("Workspace pager: a live minimap of every workspace, drag windows between them.");
        var socketOption = cli.Add(CommonOptions.Socket());

        return cli.Run(args, result =>
        {
            using var loggers = cli.CreateLoggerFactory(result);
            return Run(loggers.CreateLogger("WorkspacePager"), result.GetValue(socketOption));
        });
    }

    private static int Run(ILogger log, string? socket)
    {
        Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        using var display = socket is null ? WlDisplay.Connect() : WlDisplay.Connect(socket);
        var registry = display.GetRegistry();

        WlCompositor? compositor = null;
        WlShm? shm = null;
        XdgWmBase? wmBase = null;
        ZwlrLayerShellV1? layerShell = null;
        WlSeat? seat = null;
        var hasPointer = false;
        ExtWorkspaceManagerV1? manager = null;
        OrgKdePlasmaWindowManagement? windowManagement = null;

        var outputs = new List<OutputInfo>();
        var groups = new List<GroupEntry>();
        var workspaces = new List<WorkspaceEntry>();
        var windows = new List<PlasmaWin>();
        var cells = new List<CellHit>();
        var needRedraw = false;
        var pendingCreate = false;
        var surfacesReady = false;
        var requested = (Width: 0, Height: 0);

        void WireOutput(uint name, WlOutput proxy)
        {
            var info = new OutputInfo { Proxy = proxy, GlobalName = name };
            outputs.Add(info);
            CreatePagerSurface(info);
            proxy.Geometry += (_, e) =>
            {
                info.X = e.X;
                info.Y = e.Y;
                needRedraw = true;
            };
            proxy.ModeEvent += (_, e) =>
            {
                if ((e.Flags & WlOutput.Mode.Current) != 0)
                {
                    info.Width = e.Width;
                    info.Height = e.Height;
                    needRedraw = true;
                }
            };
            proxy.Scale += (_, e) =>
            {
                info.Scale = e.Factor;
                needRedraw = true;
            };
        }

        void CreatePagerSurface(OutputInfo info)
        {
            if (!surfacesReady || layerShell is null || compositor is null || info.Pager is not null)
            {
                return;
            }

            var pagerSurface = compositor.CreateSurface();
            var layer = layerShell.GetLayerSurface(
                pagerSurface, info.Proxy, ZwlrLayerShellV1.Layer.Bottom, "workspace-pager");
            layer.SetAnchor(ZwlrLayerSurfaceV1.Anchor.Top | ZwlrLayerSurfaceV1.Anchor.Left);
            layer.SetMargin(8, 0, 0, 8);
            layer.SetSize((uint)Math.Max(requested.Width, 1), (uint)Math.Max(requested.Height, 1));
            var pager = new PagerSurface { Surface = pagerSurface, Layer = layer };
            layer.Configure += (_, e) =>
            {
                layer.AckConfigure(e.Serial);
                pager.Configured = true;
                needRedraw = true;
            };
            layer.Closed += (_, _) => DestroyPagerSurface(info);
            info.Pager = pager;
            pagerSurface.Commit();
            display.Flush();
        }

        void DestroyPagerSurface(OutputInfo info)
        {
            if (info.Pager is not { } pager)
            {
                return;
            }

            info.Pager = null;
            pager.Layer.Destroy();
            pager.Surface.Destroy();
            pager.Shown?.Dispose();
        }

        void WireGroup(ExtWorkspaceGroupHandleV1 handle)
        {
            var group = new GroupEntry { Handle = handle };
            groups.Add(group);
            handle.Capabilities += (_, e) =>
                group.CanCreate = (e.Capabilities & ExtWorkspaceGroupHandleV1.GroupCapabilities.CreateWorkspace) != 0;
            handle.OutputEnter += (_, e) =>
                group.Output = outputs.Find(o => ReferenceEquals(o.Proxy, e.Output));
            handle.WorkspaceEnter += (_, e) =>
            {
                var entry = workspaces.Find(w => ReferenceEquals(w.Handle, e.Workspace));
                if (entry is not null)
                {
                    entry.Group?.Members.Remove(entry);
                    entry.Group = group;
                    group.Members.Add(entry);
                }
            };
            handle.WorkspaceLeave += (_, e) =>
            {
                var entry = workspaces.Find(w => ReferenceEquals(w.Handle, e.Workspace));
                if (entry is not null && entry.Group == group)
                {
                    group.Members.Remove(entry);
                    entry.Group = null;
                }
            };
            handle.Removed += (_, _) =>
            {
                foreach (var member in group.Members)
                {
                    member.Group = null;
                }

                groups.Remove(group);
                handle.Destroy();
            };
        }

        void WireWorkspace(ExtWorkspaceHandleV1 handle)
        {
            var entry = new WorkspaceEntry { Handle = handle };
            workspaces.Add(entry);
            handle.IdEvent += (_, e) => entry.Id = e.Id;
            handle.Name += (_, e) => entry.Name = e.Name;
            handle.StateEvent += (_, e) => entry.State = (uint)e.State;
            handle.Coordinates += (_, e) =>
            {
                var coordinates = MemoryMarshal.Cast<byte, uint>(e.Coordinates);
                entry.Position = coordinates.Length > 0 ? coordinates[0] : null;
            };
            handle.Removed += (_, _) =>
            {
                entry.Group?.Members.Remove(entry);
                workspaces.Remove(entry);
                handle.Destroy();
            };
        }

        void WireWindow(uint legacyId, string uuid)
        {
            var window = windowManagement!.GetWindowByUuid(uuid);
            var win = new PlasmaWin { Uuid = uuid, Window = window };
            windows.Add(win);
            window.Geometry += (_, e) =>
            {
                win.X = e.X;
                win.Y = e.Y;
                win.Width = (int)e.Width;
                win.Height = (int)e.Height;
                needRedraw = true;
            };
            window.StateChanged += (_, e) =>
            {
                win.Active = (e.Flags & (uint)OrgKdePlasmaWindowManagement.State.Active) != 0;
                needRedraw = true;
            };
            window.VirtualDesktopEntered += (_, e) =>
            {
                win.Desktops.Add(e.Id);
                needRedraw = true;
            };
            window.VirtualDesktopLeft += (_, e) =>
            {
                win.Desktops.Remove(e.Is);
                needRedraw = true;
            };
            window.Unmapped += (_, _) =>
            {
                windows.Remove(win);
                window.Destroy();
                needRedraw = true;
            };
        }

        void ReorderWindows(IReadOnlyList<string> uuids)
        {
            var ordered = new List<PlasmaWin>(windows.Count);
            foreach (var uuid in uuids)
            {
                var win = windows.Find(w => w.Uuid == uuid);
                if (win is not null && !ordered.Contains(win))
                {
                    ordered.Add(win);
                }
            }

            foreach (var win in windows)
            {
                if (!ordered.Contains(win))
                {
                    ordered.Add(win);
                }
            }

            windows.Clear();
            windows.AddRange(ordered);
            needRedraw = true;
        }

        void RequestStackingOrder()
        {
            if (windowManagement is not { } management || !management.SupportsGetStackingOrder)
            {
                return;
            }

            var order = management.GetStackingOrder();
            var uuids = new List<string>();
            order.Window += (_, e) => uuids.Add(e.Uuid);
            order.Done += (_, _) =>
            {
                ReorderWindows(uuids);
                order.Dispose();
            };
        }

        registry.Global += (_, e) =>
        {
            switch (e.Interface)
            {
                case "wl_compositor":
                    compositor = registry.Bind<WlCompositor>(e.Name, Math.Min(4u, e.Version));
                    break;
                case "wl_shm":
                    shm = registry.Bind<WlShm>(e.Name, 1);
                    break;
                case "wl_output":
                    WireOutput(e.Name, registry.Bind<WlOutput>(e.Name, Math.Min(4u, e.Version)));
                    break;
                case "xdg_wm_base":
                    wmBase = registry.Bind<XdgWmBase>(e.Name, 1);
                    break;
                case "zwlr_layer_shell_v1":
                    layerShell = registry.Bind<ZwlrLayerShellV1>(e.Name, 1);
                    break;
                case "wl_seat":
                    seat = registry.Bind<WlSeat>(e.Name, Math.Min(5u, e.Version));
                    seat.Capabilities += (_, ce) => hasPointer = (ce.Capabilities & WlSeat.Capability.Pointer) != 0;
                    break;
                case "ext_workspace_manager_v1":
                    manager = registry.Bind<ExtWorkspaceManagerV1>(e.Name, 1);
                    manager.WorkspaceGroup += (_, ge) => WireGroup(ge.WorkspaceGroup);
                    manager.Workspace += (_, we) => WireWorkspace(we.Workspace);
                    break;
                case "org_kde_plasma_window_management":
                    windowManagement = registry.Bind<OrgKdePlasmaWindowManagement>(e.Name, Math.Min(20u, e.Version));
                    windowManagement.WindowWithUuid += (_, we) => WireWindow(we.Id, we.Uuid);
                    windowManagement.StackingOrderUuidChanged += (_, se) =>
                        ReorderWindows(se.Uuids.Length == 0 ? [] : se.Uuids.Split(';'));
                    windowManagement.StackingOrderChanged2 += (_, _) => RequestStackingOrder();
                    break;
            }
        };
        registry.GlobalRemove += (_, e) =>
        {
            var info = outputs.Find(o => o.GlobalName == e.Name);
            if (info is null)
            {
                return;
            }

            DestroyPagerSurface(info);
            outputs.Remove(info);
            foreach (var group in groups)
            {
                if (ReferenceEquals(group.Output, info))
                {
                    group.Output = null;
                }
            }

            needRedraw = true;
        };

        display.Roundtrip();
        RequestStackingOrder();

        if (compositor is null || shm is null || (wmBase is null && layerShell is null))
        {
            log.LogError("compositor is missing wl_compositor, wl_shm, or both shells");
            return 1;
        }

        if (manager is null)
        {
            log.LogError("ext_workspace_manager_v1 is not advertised; nothing to page");
            return 1;
        }

        if (windowManagement is null)
        {
            log.LogWarning("org_kde_plasma_window_management is not advertised; cells stay empty");
        }

        manager.Done += (_, _) =>
        {
            foreach (var group in groups)
            {
                group.Members.Sort((a, b) => (a.Position, b.Position) switch
                {
                    (null, null) => 0,
                    (null, _) => 1,
                    (_, null) => -1,
                    var (left, right) => left.Value.CompareTo(right.Value),
                });
            }

            if (pendingCreate)
            {
                var created = workspaces.Find(w => w.Name == CreatedName && (w.State & 1) == 0);
                if (created is not null)
                {
                    pendingCreate = false;
                    Console.WriteLine($"ACTIVATE {created.Name}");
                    created.Handle.Activate();
                    manager!.Commit();
                    display.Flush();
                }
            }

            for (var i = 0; i < groups.Count; i++)
            {
                var text = groups[i].Members.Select(w =>
                    $"[{w.Name}{((w.State & 1) != 0 ? "*" : "")}{((w.State & 2) != 0 ? "!" : "")}" +
                    $":{windows.Count(win => w.Id is { } wsId && win.Desktops.Contains(wsId))}]");
                Console.WriteLine($"WORKSPACES group={i} {string.Join(" ", text)}");
            }

            needRedraw = true;
        };

        display.Roundtrip();

        var width = 0;
        var height = 0;
        var closed = false;
        var drawn = false;
        WlSurface? fallbackSurface = null;
        if (layerShell is not null)
        {
            requested = Measure(groups, cells: null);
            surfacesReady = true;
            foreach (var info in outputs)
            {
                CreatePagerSurface(info);
            }

            Console.WriteLine("MODE layer");
        }
        else
        {
            fallbackSurface = compositor.CreateSurface();
            wmBase!.Ping += (_, e) => wmBase.Pong(e.Serial);
            var xdgSurface = wmBase.GetXdgSurface(fallbackSurface);
            var toplevel = xdgSurface.GetToplevel();
            toplevel.SetTitle("Workspaces");
            toplevel.SetAppId("basin-workspace-pager");
            toplevel.Close += (_, _) => closed = true;
            xdgSurface.Configure += (_, e) =>
            {
                xdgSurface.AckConfigure(e.Serial);
                needRedraw = true;
            };
            Console.WriteLine("MODE toplevel");
        }

        PlasmaWin? dragWindow = null;
        var dragMoved = false;
        WlSurface? pointerSurface = null;
        double pointerX = 0, pointerY = 0;
        double pressX = 0, pressY = 0;

        CellHit? CellAt(double x, double y) =>
            cells.Count == 0
                ? null
                : cells.Where(c => x >= c.Left && x < c.Left + c.Width && y >= c.Top && y < c.Top + c.Height)
                    .Cast<CellHit?>()
                    .FirstOrDefault();

        PlasmaWin? WindowAt(double x, double y)
        {
            if (CellAt(x, y) is not { Workspace.Id: { } desktopId } cell)
            {
                return null;
            }

            for (var i = windows.Count - 1; i >= 0; i--)
            {
                var win = windows[i];
                if (!win.Desktops.Contains(desktopId))
                {
                    continue;
                }

                var rect = OutlineRect(cell, win);
                if (x >= rect.X && x < rect.X + rect.Width && y >= rect.Y && y < rect.Y + rect.Height)
                {
                    return win;
                }
            }

            return null;
        }

        if (hasPointer && seat is not null)
        {
            var pointer = seat.GetPointer();
            pointer.Enter += (_, e) =>
            {
                pointerSurface = e.Surface;
                (pointerX, pointerY) = (e.SurfaceX.ToDouble(), e.SurfaceY.ToDouble());
            };
            pointer.Leave += (_, _) => pointerSurface = null;
            pointer.Motion += (_, e) =>
            {
                (pointerX, pointerY) = (e.SurfaceX.ToDouble(), e.SurfaceY.ToDouble());
                if (dragWindow is not null)
                {
                    if (Math.Abs(pointerX - pressX) + Math.Abs(pointerY - pressY) > DragThreshold)
                    {
                        dragMoved = true;
                    }

                    needRedraw = true;
                }
            };
            pointer.Button += (_, e) =>
            {
                if (e.Button == InputCodes.BtnLeft && e.State == WlPointer.ButtonState.Pressed)
                {
                    (pressX, pressY) = (pointerX, pointerY);
                    dragMoved = false;
                    dragWindow = WindowAt(pointerX, pointerY);
                    if (dragWindow is not null)
                    {
                        return;
                    }
                }

                if (e.Button == InputCodes.BtnLeft && e.State == WlPointer.ButtonState.Released && dragWindow is not null)
                {
                    var grabbed = dragWindow;
                    dragWindow = null;
                    needRedraw = true;
                    var target = CellAt(pointerX, pointerY);
                    if (dragMoved && target is { IsCreate: true, Group.CanCreate: true })
                    {
                        Console.WriteLine($"DRAG {grabbed.Uuid} > new");
                        grabbed.Window.RequestEnterNewVirtualDesktop();
                        display.Flush();
                        return;
                    }

                    if (dragMoved && target is { Workspace.Id: { } desktopId } &&
                        !grabbed.Desktops.Contains(desktopId))
                    {
                        Console.WriteLine($"DRAG {grabbed.Uuid} > {desktopId}");
                        grabbed.Window.RequestEnterVirtualDesktop(desktopId);
                        display.Flush();
                        return;
                    }

                    if (!dragMoved)
                    {
                        ClickCell(target);
                    }

                    return;
                }

                if (e.State != WlPointer.ButtonState.Pressed)
                {
                    return;
                }

                var cell = CellAt(pointerX, pointerY);
                if (e.Button == InputCodes.BtnLeft)
                {
                    ClickCell(cell);
                }
                else if (e.Button == InputCodes.BtnRight && cell is { Workspace: { } workspace })
                {
                    Console.WriteLine($"REMOVE {workspace.Name}");
                    workspace.Handle.Remove();
                    manager!.Commit();
                    display.Flush();
                }
            };
        }

        void ClickCell(CellHit? cell)
        {
            if (cell is { IsCreate: true, Group.CanCreate: true })
            {
                Console.WriteLine("CREATE");
                pendingCreate = true;
                cell.Value.Group.Handle.CreateWorkspace(CreatedName);
                manager!.Commit();
                display.Flush();
            }
            else if (cell is { Workspace: { } workspace })
            {
                Console.WriteLine($"ACTIVATE {workspace.Name}");
                workspace.Handle.Activate();
                manager!.Commit();
                display.Flush();
            }
        }

        fallbackSurface?.Commit();
        display.Flush();

        ShmBuffer? fallbackShown = null;
        while (!closed)
        {
            display.Dispatch();
            if (!needRedraw || closed)
            {
                continue;
            }

            needRedraw = false;
            cells.Clear();
            (width, height) = Measure(groups, cells);
            if (fallbackSurface is null)
            {
                if ((width, height) != requested)
                {
                    requested = (width, height);
                    foreach (var info in outputs)
                    {
                        info.Pager?.Layer.SetSize((uint)width, (uint)height);
                    }
                }

                var committed = false;
                foreach (var info in outputs)
                {
                    if (info.Pager is not { Configured: true } pager)
                    {
                        continue;
                    }

                    var buffer = new ShmBuffer(shm, width, height);
                    var focused = pointerSurface == pager.Surface;
                    Paint(buffer, cells, windows, dragWindow, dragMoved,
                        focused ? pointerX : -10000, focused ? pointerY : -10000);
                    pager.Surface.Attach(buffer.Proxy, 0, 0);
                    pager.Surface.Damage(0, 0, width, height);
                    pager.Surface.Commit();
                    buffer.Proxy.Release += (_, _) => buffer.Dispose();
                    pager.Shown = buffer;
                    committed = true;
                }

                display.Flush();
                if (!committed)
                {
                    continue;
                }
            }
            else
            {
                var buffer = new ShmBuffer(shm, width, height);
                Paint(buffer, cells, windows, dragWindow, dragMoved, pointerX, pointerY);
                fallbackSurface.Attach(buffer.Proxy, 0, 0);
                fallbackSurface.Damage(0, 0, width, height);
                fallbackSurface.Commit();
                display.Flush();
                buffer.Proxy.Release += (_, _) => buffer.Dispose();
                fallbackShown = buffer;
            }

            if (!drawn)
            {
                drawn = true;
                Console.WriteLine($"MAPPED {width}x{height}");
            }
        }

        foreach (var info in outputs)
        {
            info.Pager?.Shown?.Dispose();
        }

        fallbackShown?.Dispose();
        return 0;
    }

    private static (int Width, int Height) CellSize(GroupEntry group)
    {
        var output = group.Output;
        var cellHeight = output is null
            ? CellWidth * 2 / 3
            : Math.Max(24, CellWidth * output.LogicalHeight / output.LogicalWidth);
        return (CellWidth, cellHeight);
    }

    private static (int Width, int Height) Measure(List<GroupEntry> groups, List<CellHit>? cells)
    {
        var width = CellWidth + (2 * Margin);
        var top = Margin;
        foreach (var group in groups)
        {
            var (cellWidth, cellHeight) = CellSize(group);
            var left = Margin;
            foreach (var workspace in group.Members)
            {
                cells?.Add(new CellHit(group, workspace, false, left, top, cellWidth, cellHeight));
                left += cellWidth + Gap;
            }

            if (group.CanCreate)
            {
                cells?.Add(new CellHit(group, null, true, left, top, cellWidth, cellHeight));
                left += cellWidth + Gap;
            }

            width = Math.Max(width, left - Gap + Margin);
            top += cellHeight + Gap;
        }

        var height = Math.Max(top - Gap + Margin, (2 * Margin) + 24);
        return (width, height);
    }

    private static (int X, int Y, int Width, int Height) OutlineRect(in CellHit cell, PlasmaWin win)
    {
        var output = cell.Group.Output;
        var originX = output?.X ?? 0;
        var originY = output?.Y ?? 0;
        var logicalWidth = output?.LogicalWidth ?? 1920;
        var logicalHeight = output?.LogicalHeight ?? 1280;

        var x = cell.Left + ((win.X - originX) * cell.Width / logicalWidth);
        var y = cell.Top + ((win.Y - originY) * cell.Height / logicalHeight);
        var w = Math.Max(3, win.Width * cell.Width / logicalWidth);
        var h = Math.Max(3, win.Height * cell.Height / logicalHeight);

        x = Math.Clamp(x, cell.Left, cell.Left + cell.Width - 1);
        y = Math.Clamp(y, cell.Top, cell.Top + cell.Height - 1);
        w = Math.Min(w, cell.Left + cell.Width - x);
        h = Math.Min(h, cell.Top + cell.Height - y);
        return (x, y, w, h);
    }

    private static unsafe void Paint(
        ShmBuffer buffer, List<CellHit> cells, List<PlasmaWin> windows,
        PlasmaWin? dragWindow, bool dragMoved, double pointerX, double pointerY)
    {
        Fill(buffer, 0, 0, buffer.Width, buffer.Height, 0xFF181B22);

        foreach (var cell in cells)
        {
            if (cell.IsCreate)
            {
                Fill(buffer, cell.Left, cell.Top, cell.Width, cell.Height, 0xFF2E4632);
                Cross(buffer, cell.Left + (cell.Width / 2), cell.Top + (cell.Height / 2), 0xFF9CD9A4);
                continue;
            }

            var workspace = cell.Workspace!;
            var active = (workspace.State & 1) != 0;
            var urgent = (workspace.State & 2) != 0;
            var background = urgent ? 0xFF6B3A1Fu : active ? 0xFF39445Au : 0xFF262B36u;
            Fill(buffer, cell.Left, cell.Top, cell.Width, cell.Height, background);
            Border(buffer, cell.Left, cell.Top, cell.Width, cell.Height, active ? 0xFF9DB4E8u : 0xFF3A4150u);

            if (workspace.Id is not { } desktopId)
            {
                continue;
            }

            foreach (var win in windows)
            {
                if (!win.Desktops.Contains(desktopId) || win == dragWindow)
                {
                    continue;
                }

                var (x, y, w, h) = OutlineRect(cell, win);
                Fill(buffer, x, y, w, h, win.Active ? 0x88B9CCF2u : 0x666F7A8Cu);
                Border(buffer, x, y, w, h, win.Active ? 0xFFDCE6FBu : 0xFF9AA4B5u);
            }
        }

        if (dragWindow is not null && dragMoved)
        {
            var w = Math.Max(10, CellWidth / 6);
            var h = Math.Max(8, w * 2 / 3);
            var x = (int)pointerX - (w / 2);
            var y = (int)pointerY - (h / 2);
            Fill(buffer, x, y, w, h, 0xAAB9CCF2);
            Border(buffer, x, y, w, h, 0xFFFFFFFF);
        }
    }

    private static unsafe void Fill(ShmBuffer buffer, int left, int top, int width, int height, uint color)
    {
        var alpha = color >> 24;
        for (var y = Math.Max(0, top); y < Math.Min(buffer.Height, top + height); y++)
        {
            var row = (uint*)(buffer.Data + (y * buffer.Stride));
            for (var x = Math.Max(0, left); x < Math.Min(buffer.Width, left + width); x++)
            {
                row[x] = alpha == 0xFF ? color : Blend(row[x], color);
            }
        }
    }

    private static uint Blend(uint under, uint over)
    {
        var alpha = (over >> 24) & 0xFF;
        var inverse = 255 - alpha;
        var r = ((((over >> 16) & 0xFF) * alpha) + (((under >> 16) & 0xFF) * inverse)) / 255;
        var g = ((((over >> 8) & 0xFF) * alpha) + (((under >> 8) & 0xFF) * inverse)) / 255;
        var b = (((over & 0xFF) * alpha) + ((under & 0xFF) * inverse)) / 255;
        return 0xFF000000 | (r << 16) | (g << 8) | b;
    }

    private static unsafe void Border(ShmBuffer buffer, int left, int top, int width, int height, uint color)
    {
        Fill(buffer, left, top, width, 1, color);
        Fill(buffer, left, top + height - 1, width, 1, color);
        Fill(buffer, left, top, 1, height, color);
        Fill(buffer, left + width - 1, top, 1, height, color);
    }

    private static unsafe void Cross(ShmBuffer buffer, int centerX, int centerY, uint color)
    {
        Fill(buffer, centerX - 6, centerY, 13, 1, color);
        Fill(buffer, centerX, centerY - 6, 1, 13, color);
    }
}
