using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Basin.Capabilities;

namespace Basin.UI.Avalonia;

internal sealed class BasinClipboard : IClipboard
{
    private readonly IClipboardImpl _impl;

    public BasinClipboard(IClipboardImpl impl) => _impl = impl;

    public Task ClearAsync() => _impl.ClearAsync();

    public Task SetDataAsync(IAsyncDataTransfer? dataTransfer) =>
        dataTransfer is null ? _impl.ClearAsync() : _impl.SetDataAsync(dataTransfer);

    public Task FlushAsync() =>
        _impl is IFlushableClipboardImpl flushable ? flushable.FlushAsync() : Task.CompletedTask;

    public Task<IAsyncDataTransfer?> TryGetDataAsync() => _impl.TryGetDataAsync();

    public Task<IAsyncDataTransfer?> TryGetInProcessDataAsync() => Task.FromResult<IAsyncDataTransfer?>(null);
}
