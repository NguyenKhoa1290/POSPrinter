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

    // Auto-reconnect chạy nền và cũng chiếm sóng Bluetooth — phải huỷ hẳn
    // trước khi người dùng quét thủ công, nếu không nó sẽ dừng phiên quét đó.
    private CancellationTokenSource? _autoReconnectCts;
    private Task? _autoReconnectTask;

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
        _autoReconnectCts = new CancellationTokenSource();
        _autoReconnectTask = AutoReconnectAsync(_autoReconnectCts.Token);
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

            // Panel vừa mở → CollectionView đã hiển thị, giờ mới an toàn để đổ dữ liệu
            if (!value && _pendingListDevice is { } pending)
            {
                _pendingListDevice = null;
                ShowDeviceInList(pending);
            }
        }
    }
    public bool IsBtExpanded => !_isBtCollapsed;
    public string BtArrow    => _isBtCollapsed ? "▶" : "▼";

    /// <summary>
    /// Thiết bị đã kết nối nhưng chưa đưa được vào CollectionView vì panel đang thu gọn.
    /// Sẽ được đổ vào danh sách ngay khi người dùng mở panel ra.
    /// </summary>
    private BluetoothDevice? _pendingListDevice;

    [RelayCommand]
    private void ToggleBtPanel() => IsBtCollapsed = !IsBtCollapsed;

    /// <summary>
    /// Đưa thiết bị vào danh sách và chọn nó — CHỈ khi panel đang mở.
    /// Nếu panel đang thu gọn thì giữ lại, tránh thao tác lên CollectionView
    /// chưa realize (selection bị đẩy về null, danh sách hỏng khi mở ra).
    /// </summary>
    private void ShowDeviceInList(BluetoothDevice device)
    {
        if (IsBtCollapsed)
        {
            _pendingListDevice = device;
            return;
        }

        if (!DiscoveredDevices.Any(d => d.Address == device.Address))
            DiscoveredDevices.Add(device);
        SelectedDevice = device;
    }

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

        // Nhường sóng: đợi auto-reconnect dừng hẳn rồi mới quét
        await CancelAutoReconnectAsync();

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
                : _printerService.IsBluetoothEnabled
                    ? "Không tìm thấy thiết bị nào"
                    : "⚠ Bluetooth chưa bật hoặc chưa được cấp quyền";
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

        // Không để auto-reconnect chen ngang phiên kết nối thủ công
        await CancelAutoReconnectAsync();

        IsBusy = true;
        StatusMessage = $"Đang kết nối {SelectedDevice.Name}...";
        bool ok = await _printerService.ConnectAsync(SelectedDevice);
        IsDeviceConnected = ok;
        StatusMessage = ok ? $"✓ Đã kết nối {SelectedDevice.Name}" : "✗ Kết nối thất bại";

        if (ok && SelectedDevice != null)
        {
            // Lưu ngay khi kết nối thành công → auto-reconnect lần sau
            AppPreferences.LastDeviceAddress = SelectedDevice.Address;
            AppPreferences.LastDeviceName    = SelectedDevice.Name;
            // Thu gọn khung BT — trạng thái này được lưu lại cho lần mở app sau
            IsBtCollapsed = true;
        }

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

    /// <summary>Huỷ auto-reconnect và đợi nó nhả sóng Bluetooth ra hẳn.</summary>
    private async Task CancelAutoReconnectAsync()
    {
        if (_autoReconnectTask == null) return;

        _autoReconnectCts?.Cancel();
        try { await _autoReconnectTask; } catch { /* đã huỷ */ }

        _autoReconnectCts?.Dispose();
        _autoReconnectCts = null;
        _autoReconnectTask = null;
    }

    private async Task AutoReconnectAsync(CancellationToken cancellationToken)
    {
        string lastAddr = AppPreferences.LastDeviceAddress;
        string lastName = AppPreferences.LastDeviceName;
        if (string.IsNullOrEmpty(lastAddr) || _printerService == null) return;

        // Chờ app + BLE stack khởi động hoàn tất (iOS cần lâu hơn Android)
        await Task.Delay(2500, cancellationToken);

        try
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
                StatusMessage = $"Đang kết nối lại {lastName}...");

            // 1) Đường tắt: nền tảng có thể lấy lại thiết bị đã lưu mà không cần quét
            //    (iOS/CoreBluetooth). Android/Windows trả null → rơi xuống bước quét.
            BluetoothDevice? target = await _printerService.TryGetKnownDeviceAsync(lastAddr);

            // 2) Quét tìm địa chỉ khớp.
            //    Android trả kết quả ngay trong callback (paired devices) nên TCS
            //    hoàn tất tức thì; iOS/BLE cần vài giây nên phải chờ thật sự thay vì
            //    dừng quét ngay sau khi StartScanAsync trả về.
            if (target == null && !cancellationToken.IsCancellationRequested)
            {
                var found = new TaskCompletionSource<BluetoothDevice>();
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(12));

                await _printerService.StartScanAsync(device =>
                {
                    if (device.Address == lastAddr)
                        found.TrySetResult(device);
                }, cts.Token);

                // Chờ tới khi tìm thấy, hết 12s, hoặc bị huỷ (người dùng bấm quét tay)
                var stopped = new TaskCompletionSource<bool>();
                using (cts.Token.Register(() => stopped.TrySetResult(true)))
                {
                    await Task.WhenAny(found.Task, stopped.Task);
                }

                await _printerService.StopScanAsync();

                if (found.Task.IsCompletedSuccessfully)
                    target = found.Task.Result;
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (target is { } device)
            {
                // Kết nối TRƯỚC, đụng vào UI sau. CollectionView danh sách thiết bị
                // nằm trong vùng IsBtExpanded — ghi vào nó khi panel đang thu gọn
                // là thao tác lên control chưa realize, sẽ hỏng khi mở panel ra.
                bool ok = await _printerService.ConnectAsync(device);

                if (ok)
                {
                    // Làm mới thông tin đã lưu (tên máy in có thể đổi)
                    AppPreferences.LastDeviceAddress = device.Address;
                    AppPreferences.LastDeviceName    = device.Name;
                }

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    IsDeviceConnected = ok;
                    StatusMessage = ok
                        ? $"✓ Tự động kết nối {lastName}"
                        : "Không thể kết nối lại — thử thủ công";

                    if (ok) ShowDeviceInList(device);
                });
            }
            else
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                    StatusMessage = "Chưa kết nối máy in");
            }
        }
        catch (OperationCanceledException)
        {
            // Người dùng chủ động quét/kết nối tay — không ghi đè trạng thái
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
