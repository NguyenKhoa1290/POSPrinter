using CommunityToolkit.Mvvm.ComponentModel;

namespace POSPrinter.Models;

/// <summary>
/// Dòng sản phẩm trong hóa đơn — hỗ trợ nhập liệu trực tiếp trên 2 cột.
/// </summary>
public partial class InvoiceItem : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalPrice))]
    private string _name = string.Empty;

    /// <summary>Số lượng — mặc định 1, người dùng không cần nhập</summary>
    public int Quantity { get; set; } = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalPrice))]
    [NotifyPropertyChangedFor(nameof(PriceText))]
    private decimal _unitPrice;

    /// <summary>
    /// Chuỗi giá tiền dùng cho Entry binding (TwoWay).
    /// Tự động parse thành UnitPrice khi người dùng nhập.
    /// </summary>
    public string PriceText
    {
        get => UnitPrice == 0 ? string.Empty : UnitPrice.ToString("N0").Replace(",", "");
        set
        {
            // Xoá dấu phẩy/chấm trước khi parse
            var clean = value?.Replace(",", "").Replace(".", "").Trim() ?? "";
            if (decimal.TryParse(clean, out decimal result))
                UnitPrice = result;
            else if (string.IsNullOrWhiteSpace(clean))
                UnitPrice = 0;
            OnPropertyChanged();
        }
    }

    /// <summary>Thành tiền = Số lượng × Đơn giá</summary>
    public decimal TotalPrice => Quantity * UnitPrice;
}
