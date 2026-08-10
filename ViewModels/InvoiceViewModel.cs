using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using POSPrinter.Models;
using POSPrinter.Services;

namespace POSPrinter.ViewModels;

public partial class InvoiceViewModel : ObservableObject
{
    private readonly IBluetoothPrinterService? _printerService;
    private CancellationTokenSource? _scanCts;

    /// <summary>Constructor không tham số dùng cho design-time.</summary>
    public InvoiceViewModel() : this(new StubBluetoothPrinterService()) { }

    /// <summary>Constructor chính — được DI inject.</summary>
    public InvoiceViewModel(IBluetoothPrinterService printerService)
    {
        _printerService = printerService;

        // Load từ preferences
        _storeName    = AppPreferences.StoreName;
        _storeAddress = AppPreferences.StoreAddress;
        _cashier      = AppPreferences.Cashier;
        _note         = AppPreferences.Note;
        _fontScale    = AppPreferences.FontScale;
        _isBtCollapsed = AppPreferences.BluetoothPanelCollapsed;
        _isStoreInfoCollapsed = AppPreferences.StoreInfoPanelCollapsed;
        _invoiceNumber = GenerateInvoiceNumber();

        // Dữ liệu mẫu nội dung
        _itemsText  = "";
        _pricesText = "";

        // Tự động kết nối thiết bị lần trước (background)
        _ = AutoReconnectAsync();
    }

    // ─── Bluetooth ────────────────────────────────────────────────────────────

    [ObservableProperty]
    private ObservableCollection<BluetoothDevice> _discoveredDevices = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    [NotifyPropertyChangedFor(nameof(ConnectionStatusText))]
    [NotifyPropertyChangedFor(nameof(ConnectionStatusColor))]
    private BluetoothDevice? _selectedDevice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnected))]
    [NotifyPropertyChangedFor(nameof(ConnectionStatusText))]
    [NotifyPropertyChangedFor(nameof(ConnectionStatusColor))]
    private bool _isDeviceConnected;

    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "Chưa kết nối máy in";

    // ─── BT panel collapse ───────────────────────────────────────────────────

    private bool _isBtCollapsed;
    public bool IsBtCollapsed
    {
        get => _isBtCollapsed;
        set
        {
            if (_isBtCollapsed == value) return;
            _isBtCollapsed = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BtArrow));
            OnPropertyChanged(nameof(IsBtExpanded));
            AppPreferences.BluetoothPanelCollapsed = value;
        }
    }
    public bool IsBtExpanded => !_isBtCollapsed;
    public string BtArrow    => _isBtCollapsed ? "▶" : "▼";

    [RelayCommand]
    private void ToggleBtPanel() => IsBtCollapsed = !IsBtCollapsed;

    // ─── Store info panel collapse ────────────────────────────────────────────

    private bool _isStoreInfoCollapsed;
    public bool IsStoreInfoCollapsed
    {
        get => _isStoreInfoCollapsed;
        set
        {
            if (_isStoreInfoCollapsed == value) return;
            _isStoreInfoCollapsed = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StoreInfoArrow));
            OnPropertyChanged(nameof(IsStoreInfoExpanded));
            AppPreferences.StoreInfoPanelCollapsed = value;
        }
    }
    public bool IsStoreInfoExpanded => !_isStoreInfoCollapsed;
    public string StoreInfoArrow    => _isStoreInfoCollapsed ? "▶" : "▼";

    [RelayCommand]
    private void ToggleStoreInfoPanel() => IsStoreInfoCollapsed = !IsStoreInfoCollapsed;

    [ObservableProperty] private string _storeName;
    [ObservableProperty] private string _storeAddress;
    [ObservableProperty] private string _cashier;
    [ObservableProperty] private string _note;
    [ObservableProperty] private string _invoiceNumber;

    // ─── Cỡ chữ in ───────────────────────────────────────────────────────────

    private float _fontScale;
    public float FontScale
    {
        get => _fontScale;
        set
        {
            if (Math.Abs(_fontScale - value) < 0.01f) return;
            _fontScale = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FontScaleLabel));
        }
    }
    public string FontScaleLabel => $"Cỡ chữ: {(int)(_fontScale * 100)}%";

    // ─── Encoding máy in ─────────────────────────────────────────────────────

    public List<string> EncodingOptions { get; } =
    [
        "★ CP1258 + ESC t 33 — EPSON (khuyên dùng)",
        "ASCII (bỏ dấu) — mọi máy in",
        "UTF-8 Raw — Xprinter/HPRT mới",
        "CP1258 + ESC t 30 — firmware cũ",
        "CP1258 + ESC t 16 — firmware khác",
        "CP1258 + ESC t 6  — firmware khác",
        "CP1258 không ESC t — máy tự cấu hình",
    ];

    private string _selectedEncodingName = "★ CP1258 + ESC t 33 — EPSON (khuyên dùng)";
    public string SelectedEncodingName
    {
        get => _selectedEncodingName;
        set
        {
            if (_selectedEncodingName == value) return;
            _selectedEncodingName = value;
            OnPropertyChanged();
            EscPosBuilder.CurrentEncoding = EncodingOptions.IndexOf(value) switch
            {
                0 => POSPrinter.Services.PrinterEncoding.Cp1258_Page33,
                1 => POSPrinter.Services.PrinterEncoding.AsciiNoDiacritics,
                2 => POSPrinter.Services.PrinterEncoding.Utf8Raw,
                3 => POSPrinter.Services.PrinterEncoding.Cp1258_Page30,
                4 => POSPrinter.Services.PrinterEncoding.Cp1258_Page16,
                5 => POSPrinter.Services.PrinterEncoding.Cp1258_Page6,
                6 => POSPrinter.Services.PrinterEncoding.Cp1258_NoCmd,
                _ => POSPrinter.Services.PrinterEncoding.Cp1258_Page33,
            };
        }
    }

    // ─── Nội dung hóa đơn — 2 cột văn bản ────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewLines))]
    private string _itemsText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GrandTotal))]
    [NotifyPropertyChangedFor(nameof(GrandTotalText))]
    [NotifyPropertyChangedFor(nameof(PreviewLines))]
    private string _pricesText = string.Empty;

    // ─── Computed ─────────────────────────────────────────────────────────────

    public bool   IsConnected          => IsDeviceConnected && _printerService?.ConnectedDevice != null;
    public string ConnectionStatusText => IsConnected ? $"✓ {_printerService?.ConnectedDevice?.Name ?? "Máy in"}" : "Chưa kết nối";
    public string ConnectionStatusColor => IsConnected ? "#00E676" : "#FF5252";

    public decimal GrandTotal    => ParsePricesSum();
    public string  GrandTotalText => $"{GrandTotal:N0} ";

    public List<(string Name, string Price)> PreviewLines
    {
        get
        {
            var names  = SplitLines(ItemsText);
            var prices = SplitLines(PricesText);
            int count  = Math.Max(names.Count, prices.Count);
            var result = new List<(string, string)>();
            for (int i = 0; i < count; i++)
            {
                string name  = i < names.Count  ? names[i]  : string.Empty;
                string price = i < prices.Count ? prices[i] : string.Empty;
                if (!string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(price))
                    result.Add((name, price));
            }
            return result;
        }
    }

    // ─── Commands ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private void RefreshInvoiceNumber() => InvoiceNumber = GenerateInvoiceNumber();

    [RelayCommand]
    private async Task StartScanAsync()
    {
        if (IsScanning) return;
        if (_printerService == null) { StatusMessage = "⚠ Dịch vụ Bluetooth chưa sẵn sàng"; return; }
        DiscoveredDevices.Clear();
        IsScanning = true;
        StatusMessage = "Đang tìm kiếm thiết bị...";
        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();
        try
        {
            await _printerService.StartScanAsync(device =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (!DiscoveredDevices.Any(d => d.Address == device.Address))
                        DiscoveredDevices.Add(device);
                });
            }, _scanCts.Token);
            await Task.Delay(10_000, _scanCts.Token);
        }
        catch (OperationCanceledException) { }
        finally
        {
            await _printerService.StopScanAsync();
            IsScanning = false;
            StatusMessage = DiscoveredDevices.Count > 0
                ? $"Tìm thấy {DiscoveredDevices.Count} thiết bị"
                : "Không tìm thấy thiết bị nào";
        }
    }

    [RelayCommand]
    private async Task StopScanAsync()
    {
        _scanCts?.Cancel();
        if (_printerService != null) await _printerService.StopScanAsync();
        IsScanning = false;
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (SelectedDevice == null || _printerService == null) return;
        IsBusy = true;
        StatusMessage = $"Đang kết nối {SelectedDevice.Name}...";
        bool ok = await _printerService.ConnectAsync(SelectedDevice);
        IsDeviceConnected = ok;
        StatusMessage = ok ? $"✓ Đã kết nối {SelectedDevice.Name}" : "✗ Kết nối thất bại";
        IsBusy = false;
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (_printerService != null) await _printerService.DisconnectAsync();
        IsDeviceConnected = false;
        StatusMessage = "Đã ngắt kết nối";
    }

    [RelayCommand]
    private async Task PrintInvoiceAsync()
    {
        if (_printerService == null) { StatusMessage = "⚠ Dịch vụ Bluetooth chưa sẵn sàng!"; return; }
        if (!IsConnected) { StatusMessage = "⚠ Chưa kết nối máy in!"; return; }

        var lines = PreviewLines;
        if (lines.Count == 0) { StatusMessage = "⚠ Chưa có nội dung để in!"; return; }

        IsBusy = true;
        StatusMessage = "Đang in hóa đơn...";

        try
        {
            byte[] data = BitmapPrinter.BuildFromText(
                storeName     : StoreName,
                storeAddress  : StoreAddress,
                invoiceNumber : InvoiceNumber,
                createdAt     : DateTime.Now,
                cashier       : Cashier,
                lines         : lines,
                grandTotal    : GrandTotal,
                note          : Note,
                fontScale     : FontScale);

            bool ok = await _printerService.PrintAsync(data);
            StatusMessage = ok ? "✓ In thành công!" : "✗ Lỗi khi gửi đến máy in";

            if (ok)
            {
                // Lưu preferences sau khi in thành công
                SavePreferences();
                InvoiceNumber = GenerateInvoiceNumber();

                // Lưu thiết bị đang kết nối để auto-reconnect lần sau
                if (SelectedDevice != null)
                {
                    AppPreferences.LastDeviceAddress = SelectedDevice.Address;
                    AppPreferences.LastDeviceName    = SelectedDevice.Name;
                    // Thu gọn BT panel sau khi in (đã kết nối rồi)
                    IsBtCollapsed = true;
                }
            }
        }
        catch (Exception ex) { StatusMessage = $"✗ {ex.Message}"; }
        finally { IsBusy = false; }
    }

    // ─── Auto-reconnect ──────────────────────────────────────────────────────

    private async Task AutoReconnectAsync()
    {
        string lastAddr = AppPreferences.LastDeviceAddress;
        string lastName = AppPreferences.LastDeviceName;
        if (string.IsNullOrEmpty(lastAddr) || _printerService == null) return;

        // Chờ app + BLE stack khởi động hoàn tất (iOS cần lâu hơn Android)
        await Task.Delay(2500);

        try
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
                StatusMessage = $"Đang kết nối lại {lastName}...");

            // Quét paired devices, tìm địa chỉ khớp
            BluetoothDevice? target = null;
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));

            await _printerService.StartScanAsync(device =>
            {
                if (target == null && device.Address == lastAddr)
                    target = device;
            }, cts.Token);

            await _printerService.StopScanAsync();

            if (target != null)
            {
                // Cập nhật UI trên main thread
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (!DiscoveredDevices.Any(d => d.Address == target.Address))
                        DiscoveredDevices.Add(target);
                    SelectedDevice = target;
                });

                bool ok = await _printerService.ConnectAsync(target);

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    IsDeviceConnected = ok;
                    StatusMessage = ok
                        ? $"✓ Tự động kết nối {lastName}"
                        : "Không thể kết nối lại — thử thủ công";
                    if (ok) IsBtCollapsed = true;
                });
            }
            else
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                    StatusMessage = "Chưa kết nối máy in");
            }
        }
        catch
        {
            // Không crash app — chỉ hiện trạng thái mặc định
            try
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                    StatusMessage = "Chưa kết nối máy in");
            }
            catch { /* app chưa sẵn sàng */ }
        }
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private void SavePreferences()
    {
        AppPreferences.StoreName  = StoreName;
        AppPreferences.StoreAddress = StoreAddress;
        AppPreferences.Cashier    = Cashier;
        AppPreferences.Note       = Note;
        AppPreferences.FontScale  = FontScale;
    }

    private decimal ParsePricesSum()
    {
        if (string.IsNullOrWhiteSpace(PricesText)) return 0;
        return SplitLines(PricesText)
            .Where(line => TryParsePrice(line, out _))
            .Sum(line => { TryParsePrice(line, out decimal v); return v; });
    }

    private static bool TryParsePrice(string? s, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var clean = s.Trim().Replace(".", "").Replace(",", "");
        return decimal.TryParse(clean, out value) && value >= 0;
    }

    private static List<string> SplitLines(string? text)
    {
        if (string.IsNullOrEmpty(text)) return [];
        return [.. text.Split('\n').Select(l => l.TrimEnd('\r'))];
    }

    private static string GenerateInvoiceNumber()
        => $"HD{DateTime.Now:yyyyMMddHHmmss}";
}
