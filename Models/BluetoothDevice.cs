namespace POSPrinter.Models;

/// <summary>
/// Thông tin thiết bị Bluetooth được tìm thấy
/// </summary>
public class BluetoothDevice
{
    public string Name { get; set; } = "Unknown Device";
    public string Address { get; set; } = string.Empty;
    public bool IsConnected { get; set; }

    /// <summary>
    /// Đối tượng native gốc (iOS: CBPeripheral, Android: BluetoothDevice)
    /// </summary>
    public object? NativeDevice { get; set; }

    public override string ToString() => $"{Name} ({Address})";
}
