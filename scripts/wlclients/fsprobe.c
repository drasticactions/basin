/* fsprobe.exe: a fake-fullscreen game for the windowed resize round-trip check.
   It opens one borderless WS_POPUP window at the size of the primary monitor, the way
   a game in borderless fullscreen does, and logs to a file:
     START W H          the monitor size
     SIZE W H           every WM_SIZE
     MOVE X Y SX SY     every mouse move in client and screen coordinates
     DOWN X Y           every left button press in client coordinates
     PAINT W H          every paint with the client size
     CLIP L T R B       the cursor clip after each WM_SIZE, in clip mode
   Clip mode confines the cursor to the client area on every WM_SIZE, as many games do.
   Usage: fsprobe.exe SECONDS LOGPATH [clip] */
#include <windows.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static FILE *out;
static int clip;

static LRESULT CALLBACK proc(HWND h, UINT m, WPARAM w, LPARAM l)
{
    if (m == WM_PAINT) {
        PAINTSTRUCT ps; HDC dc = BeginPaint(h, &ps); RECT rc; GetClientRect(h, &rc);
        HBRUSH fill = CreateSolidBrush(RGB(0x20, 0x60, 0xA0));
        FillRect(dc, &rc, fill);
        DeleteObject(fill);
        EndPaint(h, &ps);
        fprintf(out, "PAINT %ld %ld\n", rc.right, rc.bottom); fflush(out);
        return 0;
    }
    if (m == WM_SIZE) {
        fprintf(out, "SIZE %d %d\n", LOWORD(l), HIWORD(l)); fflush(out);
        if (clip) {
            RECT rc; GetClientRect(h, &rc);
            MapWindowPoints(h, NULL, (POINT *)&rc, 2);
            ClipCursor(&rc);
            RECT now; GetClipCursor(&now);
            fprintf(out, "CLIP %ld %ld %ld %ld\n", now.left, now.top, now.right, now.bottom); fflush(out);
        }
    }
    if (m == WM_MOUSEMOVE) {
        POINT p; GetCursorPos(&p);
        fprintf(out, "MOVE %d %d %ld %ld\n", (short)LOWORD(l), (short)HIWORD(l), p.x, p.y); fflush(out);
    }
    if (m == WM_LBUTTONDOWN) { fprintf(out, "DOWN %d %d\n", (short)LOWORD(l), (short)HIWORD(l)); fflush(out); }
    if (m == WM_TIMER) { DestroyWindow(h); return 0; }
    if (m == WM_DESTROY) { PostQuitMessage(0); return 0; }
    return DefWindowProcA(h, m, w, l);
}

int main(int argc, char **argv)
{
    int seconds = argc > 1 ? atoi(argv[1]) : 60;
    out = fopen(argc > 2 ? argv[2] : "fsprobe.log", "w");
    clip = argc > 3 && strcmp(argv[3], "clip") == 0;
    int width = GetSystemMetrics(SM_CXSCREEN), height = GetSystemMetrics(SM_CYSCREEN);
    fprintf(out, "START %d %d\n", width, height); fflush(out);

    WNDCLASSA wc = { .lpfnWndProc = proc, .hInstance = GetModuleHandleA(NULL), .lpszClassName = "fsprobe",
                     .hCursor = LoadCursor(NULL, IDC_ARROW) };
    RegisterClassA(&wc);
    HWND window = CreateWindowExA(0, "fsprobe", "fsprobe", WS_POPUP | WS_VISIBLE, 0, 0, width, height,
                                  NULL, NULL, wc.hInstance, NULL);
    SetTimer(window, 1, (UINT)seconds * 1000, NULL);

    MSG msg;
    while (GetMessageA(&msg, NULL, 0, 0) > 0) { TranslateMessage(&msg); DispatchMessageA(&msg); }
    fprintf(out, "EXIT\n");
    fclose(out);
    return 0;
}
