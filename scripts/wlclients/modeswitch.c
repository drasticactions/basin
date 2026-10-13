#include <windows.h>
#include <stdio.h>
#include <stdlib.h>

static FILE *out;
static int W = 1280, H = 720;

static LRESULT CALLBACK proc(HWND h, UINT m, WPARAM w, LPARAM l)
{
    if (m == WM_PAINT) {
        PAINTSTRUCT ps; HDC dc = BeginPaint(h, &ps); RECT rc; GetClientRect(h, &rc);
        HBRUSH light = CreateSolidBrush(RGB(0xE0, 0xE0, 0xE0)), dark = CreateSolidBrush(RGB(0xC0, 0x50, 0x20));
        for (int y = 0; y < rc.bottom; y += 64)
            for (int x = 0; x < rc.right; x += 64) {
                RECT cell = { x, y, x + 64, y + 64 };
                FillRect(dc, &cell, ((x / 64) + (y / 64)) % 2 ? dark : light);
            }
        DeleteObject(light); DeleteObject(dark);
        EndPaint(h, &ps);
        fprintf(out, "PAINT %ld %ld\n", rc.right, rc.bottom); fflush(out);
        return 0;
    }
    if (m == WM_SIZE) { fprintf(out, "SIZE %d %d\n", LOWORD(l), HIWORD(l)); fflush(out); }
    if (m == WM_DESTROY) { PostQuitMessage(0); return 0; }
    return DefWindowProcA(h, m, w, l);
}

int main(int argc, char **argv)
{
    int seconds = 15;
    if (argc > 1) sscanf(argv[1], "%dx%d", &W, &H);
    if (argc > 2) seconds = atoi(argv[2]);
    out = fopen(argc > 3 ? argv[3] : "modeswitch.log", "w");
    DEVMODEA before = { .dmSize = sizeof(DEVMODEA) };
    EnumDisplaySettingsA(NULL, ENUM_CURRENT_SETTINGS, &before);
    fprintf(out, "BEFORE %lu %lu\n", before.dmPelsWidth, before.dmPelsHeight);
    DEVMODEA mode = { .dmSize = sizeof(DEVMODEA), .dmPelsWidth = (DWORD)W, .dmPelsHeight = (DWORD)H, .dmFields = DM_PELSWIDTH | DM_PELSHEIGHT };
    LONG r = ChangeDisplaySettingsA(&mode, CDS_FULLSCREEN);
    DEVMODEA now = { .dmSize = sizeof(DEVMODEA) };
    EnumDisplaySettingsA(NULL, ENUM_CURRENT_SETTINGS, &now);
    fprintf(out, "CHANGE %ld NOW %lu %lu METRICS %d %d\n", r, now.dmPelsWidth, now.dmPelsHeight, GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN));
    fflush(out);
    WNDCLASSA wc = { .lpfnWndProc = proc, .hInstance = GetModuleHandleA(NULL), .lpszClassName = "modeswitch", .hCursor = LoadCursor(NULL, IDC_ARROW) };
    RegisterClassA(&wc);
    HWND hwnd = CreateWindowExA(0, "modeswitch", "modeswitch", WS_POPUP | WS_VISIBLE, 0, 0, W, H, NULL, NULL, wc.hInstance, NULL);
    SetTimer(hwnd, 1, seconds * 1000, NULL);
    MSG msg;
    while (GetMessageA(&msg, NULL, 0, 0) > 0) {
        if (msg.message == WM_TIMER) { DestroyWindow(hwnd); continue; }
        TranslateMessage(&msg); DispatchMessageA(&msg);
    }
    ChangeDisplaySettingsA(NULL, 0);
    EnumDisplaySettingsA(NULL, ENUM_CURRENT_SETTINGS, &now);
    fprintf(out, "RESTORED %lu %lu\n", now.dmPelsWidth, now.dmPelsHeight);
    fclose(out);
    return 0;
}
