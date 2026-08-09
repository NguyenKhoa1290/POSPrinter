using POSPrinter.Models;
using POSPrinter.Services;

/// <summary>
/// Stub implementation cho các nền tảng không hỗ trợ Bluetooth.
/// </summary>
internal class StubBluetoothPrinterService : IBluetoothPrinterService
{
    public bool IsBluetoothEnabled => false;
    public BluetoothDevice? ConnectedDevice => null;
    public bool IsScanning => false;

    public Task StartScanAsync(Action<BluetoothDevice> onDeviceFound, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task StopScanAsync() => Task.CompletedTask;

    public Task<bool> ConnectAsync(BluetoothDevice device) => Task.FromResult(false);

    public Task DisconnectAsync() => Task.CompletedTask;

    public Task<bool> PrintAsync(byte[] data) => Task.FromResult(false);
}
