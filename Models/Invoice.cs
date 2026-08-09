namespace POSPrinter.Models;

/// <summary>
/// Thông tin đầy đủ của một hóa đơn bán hàng
/// </summary>
public class Invoice
{
    public string InvoiceNumber { get; set; } = GenerateInvoiceNumber();
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string StoreName { get; set; } = "CỬA HÀNG";
    public string StoreAddress { get; set; } = string.Empty;
    public string Cashier { get; set; } = string.Empty;
    public List<InvoiceItem> Items { get; set; } = [];
    public decimal Discount { get; set; }
    public decimal Tax { get; set; }
    public string Note { get; set; } = string.Empty;

    /// <summary>Tổng tiền hàng trước thuế/giảm giá</summary>
    public decimal SubTotal => Items.Sum(i => i.TotalPrice);

    /// <summary>Tổng tiền phải trả</summary>
    public decimal GrandTotal => SubTotal - Discount + Tax;

    private static string GenerateInvoiceNumber()
    {
        var now = DateTime.Now;
        return $"HD{now:yyyyMMddHHmmss}";
    }
}
