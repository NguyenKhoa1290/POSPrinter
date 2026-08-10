using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using POSPrinter.Models;

namespace POSPrinter.Services;

/// <summary>
/// Nguồn JSON dùng source generator — bắt buộc để deserialize còn chạy đúng
/// sau khi trimming/AOT cắt metadata trên iOS Release.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(InvoiceRecord))]
[JsonSerializable(typeof(List<InvoiceRecord>))]
[JsonSerializable(typeof(Dictionary<string, InvoiceRecord>))]
internal partial class InvoiceJsonContext : JsonSerializerContext { }

/// <summary>
/// Lịch sử hóa đơn đã in: lưu cục bộ trước rồi đồng bộ lên Firebase Realtime Database.
///
/// Mất mạng vẫn ghi được — hóa đơn nằm lại với Synced=false và sẽ tự đẩy lên
/// ở lần đồng bộ kế tiếp. Máy POS hay rớt wifi nên đây là đường mặc định.
/// </summary>
public class InvoiceHistoryService
{
    // Lazy: FileSystem.AppDataDirectory không dùng được ở design-time
    private string? _localPathCache;
    private string LocalPath =>
        _localPathCache ??= Path.Combine(FileSystem.AppDataDirectory, "invoice_history.json");

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly SemaphoreSlim _fileGate = new(1, 1);

    /// <summary>Có cấu hình Firebase hay không (không có thì chỉ chạy cục bộ).</summary>
    public bool IsCloudEnabled => FirebaseConfig.IsConfigured;

    // ─── Lưu trữ cục bộ ───────────────────────────────────────────────────────

    public async Task<List<InvoiceRecord>> LoadLocalAsync()
    {
        await _fileGate.WaitAsync();
        try
        {
            if (!File.Exists(LocalPath)) return [];

            string json = await File.ReadAllTextAsync(LocalPath);
            if (string.IsNullOrWhiteSpace(json)) return [];

            return JsonSerializer.Deserialize(json, InvoiceJsonContext.Default.ListInvoiceRecord) ?? [];
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[History] đọc file lỗi: {ex.Message}");
            return [];
        }
        finally { _fileGate.Release(); }
    }

    private async Task SaveLocalAsync(List<InvoiceRecord> records)
    {
        await _fileGate.WaitAsync();
        try
        {
            // Chỉ giữ lại một lượng vừa phải để file không phình vô hạn
            var trimmed = records
                .OrderByDescending(r => r.CreatedAt)
                .Take(FirebaseConfig.HistoryLimit * 4)
                .ToList();

            string json = JsonSerializer.Serialize(trimmed, InvoiceJsonContext.Default.ListInvoiceRecord);
            await File.WriteAllTextAsync(LocalPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[History] ghi file lỗi: {ex.Message}");
        }
        finally { _fileGate.Release(); }
    }

    // ─── Thêm hóa đơn mới ─────────────────────────────────────────────────────

    /// <summary>
    /// Ghi hóa đơn xuống máy ngay lập tức, rồi cố đẩy lên Firebase.
    /// Trả về true nếu đã lên được cloud.
    /// </summary>
    public async Task<bool> AddAsync(InvoiceRecord record)
    {
        var all = await LoadLocalAsync();
        all.RemoveAll(r => r.Id == record.Id);
        all.Add(record);
        await SaveLocalAsync(all);

        bool pushed = await PushAsync(record);
        if (pushed)
        {
            record.Synced = true;
            await MarkSyncedAsync(record.Id);
        }
        return pushed;
    }

    private async Task MarkSyncedAsync(string id)
    {
        var all = await LoadLocalAsync();
        var item = all.FirstOrDefault(r => r.Id == id);
        if (item == null) return;

        item.Synced = true;
        await SaveLocalAsync(all);
    }

    // ─── Firebase REST ────────────────────────────────────────────────────────

    /// <summary>PUT /invoices/{id}.json — ghi đè theo khóa, gọi lại nhiều lần vẫn an toàn.</summary>
    public async Task<bool> PushAsync(InvoiceRecord record)
    {
        if (!IsCloudEnabled) return false;

        try
        {
            string url  = FirebaseConfig.BuildUrl($"{FirebaseConfig.InvoicesPath}/{record.Id}");
            string json = JsonSerializer.Serialize(record, InvoiceJsonContext.Default.InvoiceRecord);

            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _http.PutAsync(url, content);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[History] push lỗi: {ex.Message}");
            return false;
        }
    }

    /// <summary>Đẩy nốt các hóa đơn còn Synced=false. Trả về số bản đã đẩy thành công.</summary>
    public async Task<int> SyncPendingAsync()
    {
        if (!IsCloudEnabled) return 0;

        var all = await LoadLocalAsync();
        var pending = all.Where(r => !r.Synced).OrderBy(r => r.CreatedAt).ToList();
        if (pending.Count == 0) return 0;

        int done = 0;
        foreach (var record in pending)
        {
            if (!await PushAsync(record)) break;   // mất mạng → dừng, để lần sau
            record.Synced = true;
            done++;
        }

        if (done > 0) await SaveLocalAsync(all);
        return done;
    }

    /// <summary>
    /// GET /invoices.json?orderBy="$key"&amp;limitToLast=N — lấy N hóa đơn mới nhất.
    /// Sắp theo khóa nên không cần khai báo index trong security rules.
    /// </summary>
    public async Task<List<InvoiceRecord>> FetchRemoteAsync()
    {
        if (!IsCloudEnabled) return [];

        try
        {
            string url = FirebaseConfig.BuildUrl(
                FirebaseConfig.InvoicesPath,
                $"orderBy=%22%24key%22&limitToLast={FirebaseConfig.HistoryLimit}");

            using var response = await _http.GetAsync(url);
            if (!response.IsSuccessStatusCode) return [];

            string json = await response.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(json) || json == "null") return [];

            var map = JsonSerializer.Deserialize(json, InvoiceJsonContext.Default.DictionaryStringInvoiceRecord);
            if (map == null) return [];

            foreach (var (key, value) in map)
            {
                if (string.IsNullOrEmpty(value.Id)) value.Id = key;
                value.Synced = true;
            }

            return [.. map.Values];
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[History] fetch lỗi: {ex.Message}");
            return [];
        }
    }

    // ─── Hợp nhất cục bộ + cloud ──────────────────────────────────────────────

    /// <summary>
    /// Danh sách hiển thị: gộp bản cục bộ với bản trên cloud, khử trùng theo Id,
    /// mới nhất lên đầu. Bản cục bộ chưa đồng bộ vẫn hiện (kèm dấu ⏳).
    /// </summary>
    public async Task<List<InvoiceRecord>> GetHistoryAsync(bool includeRemote = true)
    {
        var local = await LoadLocalAsync();

        if (includeRemote && IsCloudEnabled)
        {
            await SyncPendingAsync();
            local = await LoadLocalAsync();

            var remote = await FetchRemoteAsync();
            var byId = local.ToDictionary(r => r.Id);

            foreach (var r in remote)
                byId[r.Id] = r;   // bản trên cloud là bản chuẩn

            local = [.. byId.Values];
        }

        return [.. local
            .OrderByDescending(r => r.CreatedAt)
            .Take(FirebaseConfig.HistoryLimit)];
    }

    /// <summary>
    /// Xóa một hóa đơn ở CẢ hai nơi: Firebase và bộ nhớ máy.
    ///
    /// Nếu có cấu hình cloud mà xóa trên Firebase thất bại (mất mạng, rules chặn)
    /// thì KHÔNG xóa bản cục bộ — xóa mỗi bản trong máy sẽ khiến hóa đơn sống lại
    /// ở lần tải danh sách kế tiếp, trông như xóa không ăn.
    /// </summary>
    /// <returns>(thành công, thông báo lỗi nếu có)</returns>
    public async Task<(bool Ok, string? Error)> DeleteAsync(InvoiceRecord record)
    {
        if (IsCloudEnabled)
        {
            try
            {
                string url = FirebaseConfig.BuildUrl($"{FirebaseConfig.InvoicesPath}/{record.Id}");
                using var response = await _http.DeleteAsync(url);

                if (!response.IsSuccessStatusCode)
                    return (false, $"Firebase từ chối xóa ({(int)response.StatusCode})");
            }
            catch (Exception ex)
            {
                return (false, $"Không kết nối được Firebase: {ex.Message}");
            }
        }

        var all = await LoadLocalAsync();
        all.RemoveAll(r => r.Id == record.Id);
        await SaveLocalAsync(all);

        return (true, null);
    }

    /// <summary>Xóa lịch sử trong máy. Không đụng tới dữ liệu trên Firebase.</summary>
    public async Task ClearLocalAsync()
    {
        await _fileGate.WaitAsync();
        try
        {
            if (File.Exists(LocalPath)) File.Delete(LocalPath);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[History] xóa file lỗi: {ex.Message}");
        }
        finally { _fileGate.Release(); }
    }
}
