using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace POSPrinter.Models;

/// <summary>
/// Một dòng hàng trong hóa đơn đã in.
/// </summary>
public class InvoiceLine
{
    public string Name  { get; set; } = string.Empty;
    public string Price { get; set; } = string.Empty;
}

/// <summary>
/// Bản ghi một hóa đơn đã in — lưu cục bộ và đồng bộ lên Firebase Realtime Database.
/// </summary>
public partial class InvoiceRecord : ObservableObject
{
    /// <summary>
    /// Khóa trên Firebase. Dạng "{ticks:D19}-{guid}" để thứ tự khóa trùng thứ tự
    /// thời gian → truy vấn orderBy="$key"&amp;limitToLast=N lấy đúng N hóa đơn mới nhất
    /// mà không cần khai báo index trong security rules.
    /// </summary>
    public string Id { get; set; } = NewId();

    public string   InvoiceNumber { get; set; } = string.Empty;
    public string   StoreName     { get; set; } = string.Empty;
    public string   Cashier       { get; set; } = string.Empty;
    public DateTime CreatedAt     { get; set; } = DateTime.Now;
    public decimal  Total         { get; set; }
    public string   Note          { get; set; } = string.Empty;
    public List<InvoiceLine> Lines { get; set; } = [];

    /// <summary>Đã đẩy lên Firebase chưa. Chỉ có ý nghĩa ở bản cục bộ.</summary>
    public bool Synced { get; set; }

    public static string NewId() =>
        $"{DateTime.UtcNow.Ticks:D19}-{Guid.NewGuid():N}";

    /// <summary>Đang mở phần chi tiết trong danh sách lịch sử hay không (chỉ dùng cho UI).</summary>
    [JsonIgnore]
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }
    private bool _isExpanded;

    // ─── Hiển thị ─────────────────────────────────────────────────────────────

    [JsonIgnore]
    public string CreatedAtText => CreatedAt.ToString("dd/MM/yyyy  HH:mm");

    [JsonIgnore]
    public string TotalText => $"{Total:N0} d";

    [JsonIgnore]
    public string ItemCountText => Lines.Count == 0
        ? "(không có dòng hàng)"
        : $"{Lines.Count} mặt hàng: {string.Join(", ", Lines.Take(3).Select(l => l.Name))}"
          + (Lines.Count > 3 ? "…" : "");

    [JsonIgnore]
    public string CashierText => string.IsNullOrWhiteSpace(Cashier) ? "—" : Cashier;

    [JsonIgnore]
    public string NoteText => string.IsNullOrWhiteSpace(Note) ? "(không có ghi chú)" : Note;

    [JsonIgnore]
    public string SyncText => Synced
        ? "Đã đồng bộ lên Firebase"
        : "Chưa đồng bộ — còn nằm trong máy";

    /// <summary>Nhãn ngắn hiện trên dòng danh sách</summary>
    [JsonIgnore]
    public string SyncShort => Synced ? "Đã đồng bộ" : "Chờ đồng bộ";

    [JsonIgnore]
    public string SyncColor => Synced ? "#00E676" : "#FFB300";
}
