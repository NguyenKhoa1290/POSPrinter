namespace POSPrinter.Services;

/// <summary>
/// Cấu hình kết nối Firebase Realtime Database.
///
/// ┌─ CÁCH LẤY ────────────────────────────────────────────────────────────────┐
/// │ 1. Vào console.firebase.google.com → chọn project → Realtime Database     │
/// │ 2. Copy URL hiện ở đầu trang, dạng:                                       │
/// │      https://ten-project-default-rtdb.asia-southeast1.firebasedatabase.app│
/// │    hoặc https://ten-project.firebaseio.com                                │
/// │ 3. Dán vào DatabaseUrl bên dưới (KHÔNG có dấu / ở cuối).                  │
/// │                                                                           │
/// │ Về AuthSecret: chỉ cần nếu security rules yêu cầu xác thực.               │
/// │ Nếu rules đang để ".read"/".write" = true thì để trống là chạy được.      │
/// └───────────────────────────────────────────────────────────────────────────┘
///
/// Giá trị mặc định ở đây có thể ghi đè lúc chạy bằng AppPreferences
/// (mục CÀI ĐẶT trong app), tiện khi cần đổi database mà không build lại.
/// </summary>
public static class FirebaseConfig
{
    /// <summary>URL gốc của Realtime Database. Để trống = tắt đồng bộ, chỉ lưu cục bộ.</summary>
    public const string DefaultDatabaseUrl = "https://email-8b157-default-rtdb.firebaseio.com";

    /// <summary>Database secret / idToken. Để trống nếu database mở công khai.</summary>
    public const string DefaultAuthSecret = "zm7i8LmYwX0ZCHXfNE9kptEwTofL8FaWJkBUzxsW";

    /// <summary>Nhánh chứa hóa đơn trong database.</summary>
    public const string InvoicesPath = "invoices";

    /// <summary>Số hóa đơn tải về nhiều nhất mỗi lần.</summary>
    public const int HistoryLimit = 50;

    // ─── Giá trị đang dùng (ưu tiên cài đặt trong app) ────────────────────────

    public static string DatabaseUrl
    {
        get
        {
            string saved = AppPreferences.FirebaseUrl;
            string url = string.IsNullOrWhiteSpace(saved) ? DefaultDatabaseUrl : saved;
            return url.TrimEnd('/');
        }
    }

    public static string AuthSecret
    {
        get
        {
            string saved = AppPreferences.FirebaseSecret;
            return string.IsNullOrWhiteSpace(saved) ? DefaultAuthSecret : saved;
        }
    }

    /// <summary>Có đủ thông tin để gọi Firebase hay không.</summary>
    public static bool IsConfigured => !string.IsNullOrWhiteSpace(DatabaseUrl);

    /// <summary>Ghép URL đầy đủ cho REST API, kèm ?auth= nếu có secret.</summary>
    public static string BuildUrl(string relativePath, string? query = null)
    {
        string url = $"{DatabaseUrl}/{relativePath.TrimStart('/')}.json";

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(query))      parts.Add(query);
        if (!string.IsNullOrWhiteSpace(AuthSecret)) parts.Add($"auth={Uri.EscapeDataString(AuthSecret)}");

        return parts.Count > 0 ? $"{url}?{string.Join("&", parts)}" : url;
    }
}
