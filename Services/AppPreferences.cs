namespace POSPrinter.Services;

/// <summary>
/// Lưu trữ cài đặt app bằng MAUI Preferences (SQLite key-value nội bộ).
/// Tự động persist qua các lần mở app.
/// </summary>
public static class AppPreferences
{
    // ─── Thông tin cửa hàng ───────────────────────────────────────────────────
    public static string StoreName
    {
        get => Preferences.Default.Get("store_name", "CỬA HÀNG CỦA TÔI");
        set => Preferences.Default.Set("store_name", value);
    }
    public static string StoreAddress
    {
        get => Preferences.Default.Get("store_address", "");
        set => Preferences.Default.Set("store_address", value);
    }
    public static string Cashier
    {
        get => Preferences.Default.Get("cashier", "");
        set => Preferences.Default.Set("cashier", value);
    }
    public static string Note
    {
        get => Preferences.Default.Get("note", "Cam on quy khach!");
        set => Preferences.Default.Set("note", value);
    }

    // ─── Cỡ chữ in ───────────────────────────────────────────────────────────
    /// <summary>Hệ số cỡ chữ: 0.7 → 1.5 (mặc định 1.0)</summary>
    public static float FontScale
    {
        get => Preferences.Default.Get("font_scale", 1.0f);
        set => Preferences.Default.Set("font_scale", value);
    }

    // ─── Thiết bị Bluetooth ───────────────────────────────────────────────────
    public static string LastDeviceAddress
    {
        get => Preferences.Default.Get("last_device_address", "");
        set => Preferences.Default.Set("last_device_address", value);
    }
    public static string LastDeviceName
    {
        get => Preferences.Default.Get("last_device_name", "");
        set => Preferences.Default.Set("last_device_name", value);
    }

    // ─── UI state ─────────────────────────────────────────────────────────────
    /// <summary>Trạng thái thu gọn của khung Bluetooth</summary>
    public static bool BluetoothPanelCollapsed
    {
        get => Preferences.Default.Get("bt_collapsed", false);
        set => Preferences.Default.Set("bt_collapsed", value);
    }

    /// <summary>Trạng thái thu gọn của khung Thông tin cửa hàng</summary>
    public static bool StoreInfoPanelCollapsed
    {
        get => Preferences.Default.Get("store_info_collapsed", false);
        set => Preferences.Default.Set("store_info_collapsed", value);
    }
}
