using CoreBluetooth;
using Foundation;
using POSPrinter.Models;
using POSPrinter.Services;

namespace POSPrinter.Platforms.iOS;

/// <summary>
/// iOS implementation của IBluetoothPrinterService sử dụng CoreBluetooth BLE.
/// 
/// Lưu ý: Máy in nhiệt ESC/POS Bluetooth thường dùng SPP (Serial Port Profile)
/// trên BT Classic. Tuy nhiên trên iOS, Apple chỉ cho phép BLE (Bluetooth Low Energy)
/// qua CoreBluetooth, trừ khi dùng MFi (Made for iPhone) với ExternalAccessory.
/// 
/// Implementation này dùng CoreBluetooth BLE và tìm service UUID phổ biến của
/// máy in BLE (0x18F0 hoặc FF00) để gửi dữ liệu ESC/POS.
/// 
/// Nếu máy in chỉ hỗ trợ BT Classic SPP, cần sử dụng ExternalAccessory framework
/// và đăng ký UIPPProtocol trong Info.plist.
/// </summary>
public class IosBleBluetoothPrinterService : NSObject, IBluetoothPrinterService, ICBCentralManagerDelegate, ICBPeripheralDelegate
{
    // ─── Printer BLE Service & Characteristic UUIDs ─────────────────────────
    // Các UUID phổ biến cho BLE printer (Xprinter, GOOJPRT, iDPRT...)
    private static readonly CBUUID[] PrinterServiceUUIDs =
    [
        CBUUID.FromString("E7810A71-73AE-499D-8C15-FAA9AEF0C3F2"), // Xprinter BLE
        CBUUID.FromString("49535343-FE7D-4AE5-8FA9-9FAFD205E455"), // Generic BLE Serial
        CBUUID.FromString("0000FF00-0000-1000-8000-00805F9B34FB"), // iDPRT, GOOJPRT
        CBUUID.FromString("0000FFF0-0000-1000-8000-00805F9B34FB"), // Another common UUID
        CBUUID.FromString("00001101-0000-1000-8000-00805F9B34FB"), // SPP emulation over BLE
    ];

    private static readonly CBUUID[] PrinterWriteCharUUIDs =
    [
        CBUUID.FromString("BEF8D6C9-9C21-4C9E-B632-BD58C1009F9F"), // Xprinter write
        CBUUID.FromString("49535343-8841-43F4-A8D4-ECBE34729BB3"), // Generic write
        CBUUID.FromString("0000FF02-0000-1000-8000-00805F9B34FB"), // Write char
        CBUUID.FromString("0000FFF2-0000-1000-8000-00805F9B34FB"), // Write char variant
        CBUUID.FromString("0000FF01-0000-1000-8000-00805F9B34FB"), // iDPRT write
    ];

    // ─── State ───────────────────────────────────────────────────────────────
    private CBCentralManager? _centralManager;
    private CBPeripheral? _connectedPeripheral;
    private CBCharacteristic? _writeCharacteristic;
    private BluetoothDevice? _connectedDevice;

    private Action<BluetoothDevice>? _onDeviceFound;
    private readonly HashSet<string> _discoveredAddresses = [];

    private TaskCompletionSource<bool>? _connectTcs;
    private bool _isScanning;

    /// <summary>Hoàn tất khi CBCentralManager đạt trạng thái PoweredOn.</summary>
    private TaskCompletionSource<bool>? _poweredOnTcs;

    // ─── IBluetoothPrinterService ─────────────────────────────────────────────

    public bool IsBluetoothEnabled => _centralManager?.State == CBManagerState.PoweredOn;
    public BluetoothDevice? ConnectedDevice => _connectedDevice;
    public bool IsScanning => _isScanning;

    public async Task StartScanAsync(Action<BluetoothDevice> onDeviceFound, CancellationToken cancellationToken = default)
    {
        _onDeviceFound = onDeviceFound;
        _discoveredAddresses.Clear();

        cancellationToken.Register(() => _ = StopScanAsync());

        // CBCentralManager vừa tạo luôn ở trạng thái Unknown — phải chờ
        // delegate UpdatedState báo PoweredOn mới quét được.
        if (!await EnsureManagerReadyAsync(cancellationToken))
            return;

        if (!cancellationToken.IsCancellationRequested)
            StartScanInternal();
    }

    /// <summary>
    /// iOS không có khái niệm "paired device" như Android. Thay vào đó CoreBluetooth
    /// cho phép lấy lại peripheral đã từng kết nối bằng UUID đã lưu — nhanh và
    /// chắc chắn hơn quét, vì máy in có thể không advertise liên tục.
    /// </summary>
    public async Task<BluetoothDevice?> TryGetKnownDeviceAsync(string address)
    {
        if (string.IsNullOrWhiteSpace(address)) return null;
        if (!await EnsureManagerReadyAsync(CancellationToken.None)) return null;

        // Địa chỉ cũ có thể không phải UUID (vd. MAC lưu từ bản Android)
        if (!Guid.TryParse(address, out var guid)) return null;
        var uuid = new NSUuid(guid.ToString());

        // 1) Peripheral đã từng biết đến
        var known = _centralManager!.RetrievePeripheralsWithIdentifiers(uuid);
        var peripheral = known?.FirstOrDefault();

        // 2) Hoặc peripheral đang được hệ thống kết nối sẵn
        if (peripheral == null)
        {
            var connected = _centralManager.RetrieveConnectedPeripherals(PrinterServiceUUIDs);
            peripheral = connected?.FirstOrDefault(p => p.Identifier.AsString() == uuid.AsString());
        }

        if (peripheral == null) return null;

        return new BluetoothDevice
        {
            Name = peripheral.Name ?? "Printer",
            Address = peripheral.Identifier.ToString(),
            NativeDevice = peripheral
        };
    }

    /// <summary>Tạo CBCentralManager (nếu chưa có) và chờ tới khi PoweredOn.</summary>
    private async Task<bool> EnsureManagerReadyAsync(CancellationToken cancellationToken)
    {
        _centralManager ??= new CBCentralManager(this, null);

        if (_centralManager.State == CBManagerState.PoweredOn)
            return true;

        var tcs = _poweredOnTcs ??= new TaskCompletionSource<bool>();

        // Chờ tối đa 10s: người dùng có thể đang bật Bluetooth / cấp quyền
        var timeout = Task.Delay(10_000, cancellationToken);
        var completed = await Task.WhenAny(tcs.Task, timeout);

        return completed == tcs.Task && _centralManager.State == CBManagerState.PoweredOn;
    }

    public Task StopScanAsync()
    {
        _centralManager?.StopScan();
        _isScanning = false;
        return Task.CompletedTask;
    }

    public async Task<bool> ConnectAsync(BluetoothDevice device)
    {
        if (device.NativeDevice is not CBPeripheral peripheral)
            return false;

        if (!await EnsureManagerReadyAsync(CancellationToken.None))
            return false;

        await StopScanAsync();

        _connectTcs = new TaskCompletionSource<bool>();
        _writeCharacteristic = null;
        _connectedPeripheral = peripheral;
        _connectedPeripheral.Delegate = this;
        _centralManager!.ConnectPeripheral(peripheral);

        // Timeout 15 giây (peripheral lấy lại từ bộ nhớ có thể cần đánh thức)
        var timeout = Task.Delay(15_000);
        var completed = await Task.WhenAny(_connectTcs.Task, timeout);

        if (completed == timeout)
        {
            _centralManager.CancelPeripheralConnection(peripheral);
            return false;
        }

        return await _connectTcs.Task;
    }

    public Task DisconnectAsync()
    {
        if (_connectedPeripheral != null && _centralManager != null)
        {
            _centralManager.CancelPeripheralConnection(_connectedPeripheral);
        }

        _connectedPeripheral = null;
        _writeCharacteristic = null;
        _connectedDevice = null;
        return Task.CompletedTask;
    }

    public async Task<bool> PrintAsync(byte[] data)
    {
        if (_connectedPeripheral == null || _writeCharacteristic == null)
            return false;

        // Chỉ dùng WithoutResponse khi characteristic thực sự hỗ trợ,
        // ngược lại iOS sẽ âm thầm bỏ qua dữ liệu.
        var writeType = _writeCharacteristic.Properties.HasFlag(CBCharacteristicProperties.WriteWithoutResponse)
            ? CBCharacteristicWriteType.WithoutResponse
            : CBCharacteristicWriteType.WithResponse;

        // Chia nhỏ dữ liệu thành chunk 20 bytes (MTU mặc định BLE)
        const int chunkSize = 20;
        for (int offset = 0; offset < data.Length; offset += chunkSize)
        {
            int length = Math.Min(chunkSize, data.Length - offset);
            var chunk = new byte[length];
            Array.Copy(data, offset, chunk, 0, length);

            var nsData = NSData.FromArray(chunk);
            _connectedPeripheral.WriteValue(nsData, _writeCharacteristic, writeType);

            // Delay nhỏ để tránh overrun bộ đệm BLE
            await Task.Delay(10);
        }

        return true;
    }

    // ─── CBCentralManager Delegate ────────────────────────────────────────────

    [Export("centralManagerDidUpdateState:")]
    public void UpdatedState(CBCentralManager central)
    {
        if (central.State == CBManagerState.PoweredOn)
        {
            _poweredOnTcs?.TrySetResult(true);
        }
        else
        {
            // Bluetooth tắt / mất quyền → reset để lần sau chờ lại
            _poweredOnTcs = null;
            _isScanning = false;
        }
    }

    [Export("centralManager:didDiscoverPeripheral:advertisementData:RSSI:")]
    public void DiscoveredPeripheral(CBCentralManager central, CBPeripheral peripheral, NSDictionary advertisementData, NSNumber RSSI)
    {
        string address = peripheral.Identifier.ToString();
        if (_discoveredAddresses.Contains(address)) return;
        _discoveredAddresses.Add(address);

        string name = peripheral.Name ?? advertisementData.ValueForKey(CBAdvertisement.DataLocalNameKey)?.ToString() ?? "Unknown Printer";

        var device = new BluetoothDevice
        {
            Name = name,
            Address = address,
            NativeDevice = peripheral
        };

        MainThread.BeginInvokeOnMainThread(() => _onDeviceFound?.Invoke(device));
    }

    [Export("centralManager:didConnectPeripheral:")]
    public void ConnectedPeripheral(CBCentralManager central, CBPeripheral peripheral)
    {
        // Khám phá TẤT CẢ service: nhiều máy in dùng UUID ngoài danh sách phổ biến,
        // lọc sẵn theo PrinterServiceUUIDs sẽ khiến kết nối treo tới khi timeout.
        peripheral.Delegate = this;
        peripheral.DiscoverServices();
    }

    [Export("centralManager:didFailToConnectPeripheral:error:")]
    public void FailedToConnectPeripheral(CBCentralManager central, CBPeripheral peripheral, NSError? error)
    {
        _connectTcs?.TrySetResult(false);
    }

    [Export("centralManager:didDisconnectPeripheral:error:")]
    public void DisconnectedPeripheral(CBCentralManager central, CBPeripheral peripheral, NSError? error)
    {
        _connectedDevice = null;
        _writeCharacteristic = null;

        // Nếu đang chờ kết nối mà bị rớt → giải phóng ConnectAsync ngay
        _connectTcs?.TrySetResult(false);
    }

    // ─── CBPeripheral Delegate ────────────────────────────────────────────────

    [Export("peripheral:didDiscoverServices:")]
    public void DiscoveredService(CBPeripheral peripheral, NSError? error)
    {
        if (error != null || peripheral.Services == null) return;

        foreach (var service in peripheral.Services)
        {
            peripheral.DiscoverCharacteristics(service);
        }
    }

    [Export("peripheral:didDiscoverCharacteristicsForService:error:")]
    public void DiscoveredCharacteristic(CBPeripheral peripheral, CBService service, NSError? error)
    {
        if (error != null || service.Characteristics == null) return;
        if (_writeCharacteristic != null) return;   // đã tìm được ở service khác

        // Ưu tiên characteristic nằm trong danh sách UUID máy in đã biết,
        // sau đó mới tới bất kỳ characteristic nào ghi được.
        CBCharacteristic? fallback = null;

        foreach (var characteristic in service.Characteristics)
        {
            bool isWritable = characteristic.Properties.HasFlag(CBCharacteristicProperties.Write)
                           || characteristic.Properties.HasFlag(CBCharacteristicProperties.WriteWithoutResponse);
            if (!isWritable) continue;

            if (PrinterWriteCharUUIDs.Any(u => u.Equals(characteristic.UUID)))
            {
                UseWriteCharacteristic(peripheral, characteristic);
                return;
            }

            fallback ??= characteristic;
        }

        if (fallback != null)
            UseWriteCharacteristic(peripheral, fallback);
    }

    private void UseWriteCharacteristic(CBPeripheral peripheral, CBCharacteristic characteristic)
    {
        _writeCharacteristic = characteristic;
        _connectedDevice = new BluetoothDevice
        {
            Name = peripheral.Name ?? "Printer",
            Address = peripheral.Identifier.ToString(),
            IsConnected = true,
            NativeDevice = peripheral
        };
        _connectTcs?.TrySetResult(true);
    }

    // ─── Private Helpers ───────────────────────────────────────────────────────

    private void StartScanInternal()
    {
        _isScanning = true;
        // Scan tất cả thiết bị BLE (không lọc UUID để tìm được nhiều loại máy in hơn)
        _centralManager?.ScanForPeripherals(peripheralUuids: null, new PeripheralScanningOptions
        {
            AllowDuplicatesKey = false
        });
    }
}
