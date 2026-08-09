#if WINDOWS
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

using AppDevice = POSPrinter.Models.BluetoothDevice;
using POSPrinter.Services;

namespace POSPrinter.Platforms.Windows;

/// <summary>
/// Bluetooth RFCOMM printer service dùng Windows WinRT API (Windows.Devices.Bluetooth).
/// Hỗ trợ Windows 10 build 19041+ mà không cần thư viện bên ngoài.
/// </summary>
public class WindowsBluetoothPrinterService : IBluetoothPrinterService
{
    private StreamSocket?          _socket;
    private DataWriter?            _writer;
    private AppDevice?             _connectedDevice;
    private bool                   _isScanning;

    // SPP UUID
    private static readonly Guid SppUuid = new("00001101-0000-1000-8000-00805F9B34FB");

    // ─── Interface ───────────────────────────────────────────────────────────

    public bool IsBluetoothEnabled
    {
        get
        {
            try
            {
                var adapter = BluetoothAdapter.GetDefaultAsync().AsTask().GetAwaiter().GetResult();
                return adapter != null;
            }
            catch { return false; }
        }
    }

    public bool      IsScanning      => _isScanning;
    public AppDevice? ConnectedDevice => _connectedDevice;

    // ─── Scan paired devices ──────────────────────────────────────────────────

    public async Task StartScanAsync(Action<AppDevice> onDeviceFound,
                                     CancellationToken ct = default)
    {
        _isScanning = true;
        try
        {
            // Lấy tất cả thiết bị Bluetooth đã ghép đôi
            string selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
            var devices = await DeviceInformation.FindAllAsync(selector);

            foreach (var info in devices)
            {
                if (ct.IsCancellationRequested) break;
                onDeviceFound(new AppDevice
                {
                    Name    = info.Name,
                    Address = info.Id  // dùng DeviceId để kết nối lại
                });
            }
        }
        catch { }
        finally { _isScanning = false; }
    }

    public Task StopScanAsync()
    {
        _isScanning = false;
        return Task.CompletedTask;
    }

    // ─── Connect ──────────────────────────────────────────────────────────────

    public async Task<bool> ConnectAsync(AppDevice device)
    {
        try
        {
            await DisconnectAsync();

            // Lấy BluetoothDevice từ DeviceId
            BluetoothDevice btDevice;
            try
            {
                btDevice = await BluetoothDevice.FromIdAsync(device.Address);
            }
            catch
            {
                // Nếu device.Address là MAC thì thử convert
                return false;
            }

            if (btDevice == null) return false;

            // Lấy RFCOMM service (SPP)
            var rfcommServices = await btDevice.GetRfcommServicesForIdAsync(
                RfcommServiceId.FromUuid(SppUuid),
                BluetoothCacheMode.Uncached);

            if (rfcommServices.Services.Count == 0) return false;

            var service = rfcommServices.Services[0];

            // Kết nối socket
            _socket = new StreamSocket();
            await _socket.ConnectAsync(service.ConnectionHostName,
                                       service.ConnectionServiceName,
                                       SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication);

            _writer          = new DataWriter(_socket.OutputStream);
            _connectedDevice = device;
            return true;
        }
        catch
        {
            _socket?.Dispose(); _socket = null;
            _writer?.Dispose(); _writer = null;
            _connectedDevice = null;
            return false;
        }
    }

    // ─── Print ────────────────────────────────────────────────────────────────

    public async Task<bool> PrintAsync(byte[] data)
    {
        if (_writer == null) return false;
        try
        {
            _writer.WriteBytes(data);
            await _writer.StoreAsync();
            await _writer.FlushAsync();
            return true;
        }
        catch { return false; }
    }

    // ─── Disconnect ───────────────────────────────────────────────────────────

    public async Task DisconnectAsync()
    {
        try
        {
            if (_writer != null)
            {
                await _writer.FlushAsync();
                _writer.Dispose();
                _writer = null;
            }
            _socket?.Dispose(); _socket = null;
            _connectedDevice = null;
        }
        catch { }
    }
}
#endif
