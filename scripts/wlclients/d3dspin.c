// d3dspin.exe [SECONDS [CHILD [LOG]]]: a D3D11 flip-model swapchain that clears to a
// changing color and presents with vsync, in the top-level window, or in a child window
// when CHILD is 1. Writes READY, FRAMES N every 60 frames and DONE N to LOG, a Windows
// path. Under Wine with DXVK and the Wayland driver it drives Mesa's Vulkan WSI, so it
// commits with explicit sync into the subsurface that Wine gives the window.
#define COBJMACROS
#define INITGUID
#include <windows.h>
#include <d3d11.h>
#include <dxgi.h>
#include <stdio.h>

static LRESULT CALLBACK proc(HWND w, UINT m, WPARAM a, LPARAM b)
{
    if (m == WM_DESTROY) { PostQuitMessage(0); return 0; }
    return DefWindowProcW(w, m, a, b);
}

int WINAPI WinMain(HINSTANCE inst, HINSTANCE prev, LPSTR cmd, int show)
{
    (void)prev; (void)show;
    int seconds = 20, child = 0;
    char log[512] = "d3dspin.log";
    sscanf(cmd, "%d %d %511s", &seconds, &child, log);
    FILE *out = fopen(log, "w");
    WNDCLASSW wc = { 0 };
    wc.lpfnWndProc = proc; wc.hInstance = inst; wc.lpszClassName = L"d3dspin";
    wc.hbrBackground = (HBRUSH)GetStockObject(BLACK_BRUSH);
    RegisterClassW(&wc);
    HWND top = CreateWindowW(L"d3dspin", L"d3dspin", WS_OVERLAPPEDWINDOW | WS_VISIBLE, 100, 100, 640, 480, NULL, NULL, inst, NULL);
    HWND target = child ? CreateWindowW(L"d3dspin", L"child", WS_CHILD | WS_VISIBLE, 40, 40, 400, 300, top, NULL, inst, NULL) : top;
    DXGI_SWAP_CHAIN_DESC desc = { 0 };
    desc.BufferCount = 2;
    desc.BufferDesc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
    desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    desc.OutputWindow = target;
    desc.SampleDesc.Count = 1;
    desc.Windowed = TRUE;
    desc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD;
    IDXGISwapChain *chain = NULL; ID3D11Device *dev = NULL; ID3D11DeviceContext *ctx = NULL;
    HRESULT hr = D3D11CreateDeviceAndSwapChain(NULL, D3D_DRIVER_TYPE_HARDWARE, NULL, 0, NULL, 0, D3D11_SDK_VERSION, &desc, &chain, &dev, NULL, &ctx);
    if (FAILED(hr)) { fprintf(out, "FAILED 0x%lx\n", hr); fclose(out); return 1; }
    ID3D11Texture2D *back = NULL; ID3D11RenderTargetView *rtv = NULL;
    IDXGISwapChain_GetBuffer(chain, 0, &IID_ID3D11Texture2D, (void **)&back);
    ID3D11Device_CreateRenderTargetView(dev, (ID3D11Resource *)back, NULL, &rtv);
    ID3D11Texture2D_Release(back);
    fprintf(out, "READY child=%d\n", child); fflush(out);
    DWORD start = GetTickCount(); unsigned frames = 0; MSG msg;
    while (GetTickCount() - start < (DWORD)seconds * 1000) {
        while (PeekMessageW(&msg, NULL, 0, 0, PM_REMOVE)) { TranslateMessage(&msg); DispatchMessageW(&msg); }
        float t = (GetTickCount() - start) / 1000.0f;
        float color[4] = { (float)(frames % 60) / 60.0f, 0.5f, t - (int)t, 1.0f };
        ID3D11DeviceContext_OMSetRenderTargets(ctx, 1, &rtv, NULL);
        ID3D11DeviceContext_ClearRenderTargetView(ctx, rtv, color);
        IDXGISwapChain_Present(chain, 1, 0);
        frames++;
        if (frames % 60 == 0) { fprintf(out, "FRAMES %u\n", frames); fflush(out); }
    }
    fprintf(out, "DONE %u\n", frames); fclose(out);
    return 0;
}
