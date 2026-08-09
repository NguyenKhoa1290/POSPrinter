using POSPrinter.Models;

namespace POSPrinter.Services;

/// <summary>
/// Interface cho dịch vụ Bluetooth - scan và in ESC/POS
/// </summary>
public interface IBluetoothPrinterService
{
    /// <summary>Trạng thái Bluetooth hiện tại</summary>
    bool IsBluetoothEnabled { get; }

    /// <summary>Thiết bị đang kết nối</summary>
    BluetoothDevice? ConnectedDevice { get; }

    /// <summary>Đang scan hay không</summary>
    bool IsScanning { get; }

    /// <summary>Bắt đầu quét thiết bị Bluetooth</summary>
    Task StartScanAsync(Action<BluetoothDevice> onDeviceFound, CancellationToken cancellationToken = default);

    /// <summary>Dừng quét</summary>
    Task StopScanAsync();

    /// <summary>Kết nối đến thiết bị</summary>
    Task<bool> ConnectAsync(BluetoothDevice device);

    /// <summary>Ngắt kết nối</summary>
    Task DisconnectAsync();

    /// <summary>Gửi dữ liệu ESC/POS đến máy in</summary>
    Task<bool> PrintAsync(byte[] data);
}
