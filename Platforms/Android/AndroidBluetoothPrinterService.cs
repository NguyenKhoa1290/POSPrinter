// Alias toàn bộ Android.Bluetooth.* để tránh collision với namespace POSPrinter.Platforms.Android
using Java.Util;
using POSPrinter.Services;
using PosDevice  = POSPrinter.Models.BluetoothDevice;
using BtAdapter  = Android.Bluetooth.BluetoothAdapter;
using BtManager  = Android.Bluetooth.BluetoothManager;
using BtDevice   = Android.Bluetooth.BluetoothDevice;
using BtSocket   = Android.Bluetooth.BluetoothSocket;
using AndroidCtx = Android.Content.Context;
using AndroidApp = Android.App.Application;

namespace POSPrinter.Platforms.Android;

/// <summary>
/// Android Bluetooth Classic SPP implementation.
/// Liệt kê thiết bị đã ghép đôi (paired) và kết nối qua RFCOMM.
/// </summary>
public class AndroidBluetoothPrinterService : IBluetoothPrinterService
{
    private static readonly UUID SppUuid =
        UUID.FromString("00001101-0000-1000-8000-00805F9B34FB")!;

    private BtSocket? _socket;
    private Stream?   _outputStream;
    private PosDevice? _connectedDevice;
    private bool      _isScanning;

    // ─── Properties ──────────────────────────────────────────────────────────

    public bool      IsBluetoothEnabled => GetAdapter()?.IsEnabled ?? false;
    public PosDevice? ConnectedDevice   => _connectedDevice;
    public bool      IsScanning         => _isScanning;

    // ─── Lấy BluetoothAdapter an toàn (tránh NullRef) ────────────────────────

    private static BtAdapter? GetAdapter()
    {
        try
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(31))
            {
                // Android 12+ — dùng BluetoothManager
                var mgr = AndroidApp.Context
                    .GetSystemService(AndroidCtx.BluetoothService) as BtManager;
                return mgr?.Adapter;
            }

            // Android < 12
#pragma warning disable CA1422
            return BtAdapter.DefaultAdapter;
#pragma warning restore CA1422
        }
        catch
        {
            return null;
        }
    }

    // ─── StartScan: liệt kê paired devices ───────────────────────────────────

    public Task StartScanAsync(
        Action<PosDevice> onDeviceFound,
        CancellationToken cancellationToken = default)
    {
        _isScanning = true;
        try
        {
            var adapter = GetAdapter();
            if (adapter is null || !adapter.IsEnabled)
            {
                _isScanning = false;
                return Task.CompletedTask;
            }

            var bonded = adapter.BondedDevices;
            if (bonded is null)
            {
                _isScanning = false;
                return Task.CompletedTask;
            }

            foreach (BtDevice? device in bonded)
            {
                if (cancellationToken.IsCancellationRequested) break;
                if (device is null) continue;

                onDeviceFound(new PosDevice
                {
                    Name         = device.Name ?? "Unknown",
                    Address      = device.Address ?? string.Empty,
                    NativeDevice = device
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[BT Scan] {ex.Message}");
        }
        finally
        {
            _isScanning = false;
        }

        return Task.CompletedTask;
    }

    public Task StopScanAsync()
    {
        _isScanning = false;
        return Task.CompletedTask;
    }

    // ─── Connect ─────────────────────────────────────────────────────────────

    public async Task<bool> ConnectAsync(PosDevice device)
    {
        if (device.NativeDevice is not BtDevice androidDevice)
            return false;

        try
        {
            await DisconnectAsync();
            _socket = androidDevice.CreateRfcommSocketToServiceRecord(SppUuid);
            await _socket!.ConnectAsync();
            _outputStream = _socket.OutputStream;

            _connectedDevice = new PosDevice
            {
                Name         = device.Name,
                Address      = device.Address,
                IsConnected  = true,
                NativeDevice = androidDevice
            };
            return true;
        }
        catch
        {
            // Fallback: reflection createRfcommSocket (một số ROM yêu cầu)
            try
            {
                var method = androidDevice.Class?
                    .GetMethod("createRfcommSocket", [Java.Lang.Integer.Type!]);
                _socket = method?.Invoke(androidDevice, [Java.Lang.Integer.ValueOf(1)])
                    as BtSocket;
                if (_socket is null) return false;

                await _socket.ConnectAsync();
                _outputStream = _socket.OutputStream;

                _connectedDevice = new PosDevice
                {
                    Name         = device.Name,
                    Address      = device.Address,
                    IsConnected  = true,
                    NativeDevice = androidDevice
                };
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    // ─── Disconnect ───────────────────────────────────────────────────────────

    public async Task DisconnectAsync()
    {
        if (_outputStream is not null)
        {
            await _outputStream.DisposeAsync();
            _outputStream = null;
        }
        _socket?.Dispose();
        _socket = null;
        _connectedDevice = null;
    }

    // ─── Print ────────────────────────────────────────────────────────────────

    public async Task<bool> PrintAsync(byte[] data)
    {
        if (_outputStream is null) return false;
        try
        {
            await _outputStream.WriteAsync(data);
            await _outputStream.FlushAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }
}
