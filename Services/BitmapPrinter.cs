using SkiaSharp;
using Microsoft.Maui.Storage;

namespace POSPrinter.Services;

/// <summary>
/// In hóa đơn bằng cách render text thành bitmap rồi gửi ESC/POS raster GS v 0.
/// Hoạt động với mọi máy in nhiệt 58mm (không cần ROM tiếng Việt).
/// Để đảm bảo font nhất quán giữa Android và Windows,
/// dùng RobotoMono-Regular.ttf + RobotoMono-Bold.ttf trong Resources/Raw/.
/// Đặt 2 file đó vào: D:\QRCode\POSPrinter\Resources\Raw\
/// Địa chỉ tải: https://fonts.google.com/specimen/Roboto+Mono
/// </summary>
public static class BitmapPrinter
{
    // ─── Font cache (load 1 lần, dùng lại mãi) ──────────────────────────────────
    private static SKTypeface? _tfRegular;
    private static SKTypeface? _tfBold;
    private static bool _fontLoaded = false;

    private static void EnsureFonts()
    {
        if (_fontLoaded) return;
        _fontLoaded = true;
        try
        {
            using var sr = FileSystem.OpenAppPackageFileAsync("Arial-Regular.ttf").GetAwaiter().GetResult();
            _tfRegular = SKTypeface.FromStream(sr);
        }
        catch { _tfRegular = null; }
        try
        {
            using var sb = FileSystem.OpenAppPackageFileAsync("Arial-Bold.ttf").GetAwaiter().GetResult();
            _tfBold = SKTypeface.FromStream(sb);
        }
        catch { _tfBold = null; }
    }

    // ─── Cấu hình ─────────────────────────────────────────────────────────────
    private const int   PrintWidthPx  = 384;   // 58mm ≈ 8 dots/mm × 48mm vùng in
    private const float FontNormal    = 22f;
    private const float FontLarge     = 30f;   // tên cửa hàng (giảm từ 40)
    private const int   LineH         = 30;
    private const int   LineHLarge    = 40;    // line height header (giảm từ 52)
    private const int   PadX         = 6;

    // ─── ESC/POS constants ────────────────────────────────────────────────────
    private const byte ESC = 0x1B;
    private const byte GS  = 0x1D;
    private const byte LF  = 0x0A;

    // ─── Public API ───────────────────────────────────────────────────────────

    public static byte[] BuildFromText(
        string storeName,
        string? storeAddress,
        string invoiceNumber,
        DateTime createdAt,
        string? cashier,
        List<(string Name, string Price)> lines,
        decimal grandTotal,
        string? note,
        float fontScale = 1.0f)
    {
        // 1. Render toàn bộ nội dung thành bitmap
        using var bmp = RenderBitmap(storeName, storeAddress, invoiceNumber,
                                     createdAt, cashier, lines, grandTotal, note, fontScale);

        // 2. Chuyển bitmap → ESC/POS raster
        var buf = new List<byte>();
        buf.AddRange([ESC, (byte)'@']);          // Reset máy in
        buf.AddRange([ESC, (byte)'3', 1]);       // Line spacing = 1 dot (khít)
        buf.AddRange(BitmapToRaster(bmp));
        buf.AddRange([ESC, (byte)'2']);           // Restore line spacing
        buf.AddRange(FeedAndCut(6));

        return [.. buf];
    }

    // ─── Render bitmap ────────────────────────────────────────────────────────

    private static SKBitmap RenderBitmap(
        string storeName, string? storeAddress, string invoiceNumber,
        DateTime createdAt, string? cashier,
        List<(string Name, string Price)> lines,
        decimal grandTotal, string? note, float fontScale = 1.0f)
    {
        // fontScale chỉ ảnh hưởng đến phần hàng hoá, tổng, footer
        // Thông tin cửa hàng và ngày giữ cỡ CỐ ĐỊNH để không bị tràn chữ
        float fFixed  = FontNormal;                    // cỡ cố định (header)
        float fLargeF = FontLarge;                     // cỡ cố định (tên shop)
        float fNormal = FontNormal * fontScale;        // cỡ scale (hàng hoá)
        float fTotal  = (FontNormal + 5) * fontScale;  // cỡ scale (tổng)

        int h = CalcHeight(storeAddress, cashier, lines, note, fontScale);
        using var surface = SKSurface.Create(
            new SKImageInfo(PrintWidthPx, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);

        int y = 8;
        // ── Header cố định (không scale) ──
        y = CenterText(canvas, storeName.ToUpper(), y, fLargeF, bold: true);
        y += 4;
        if (!string.IsNullOrWhiteSpace(storeAddress))
            y = CenterText(canvas, storeAddress, y, fFixed);

        y = HRule(canvas, y, doubled: true);
        y = LeftText(canvas, $"Ngày: {createdAt:dd/MM/yyyy HH:mm}", y, fFixed);
        y = HRule(canvas, y);

        // ── Nội dung scale theo fontScale ──
        y = TwoCol(canvas, "TÊN HÀNG", "GIÁ TIỀN", y, fNormal, bold: true);
        y = HRule(canvas, y);

        foreach (var (name, price) in lines)
        {
            string n = string.IsNullOrWhiteSpace(name)  ? "" : name;
            string p = string.IsNullOrWhiteSpace(price) ? "" : price;
            y = TwoCol(canvas, n, p, y, fNormal);
        }

        y = HRule(canvas, y, doubled: true);
        y = TwoCol(canvas, "TỔNG:", grandTotal.ToString("N0"), y, fTotal, bold: true);
        y += 10;

        y = HRule(canvas, y);
        string footer = !string.IsNullOrWhiteSpace(note) ? note : "";
        if (!string.IsNullOrWhiteSpace(footer))
            y = CenterText(canvas, footer, y, fNormal);
        y += 20;

        var snap   = surface.Snapshot();
        var bitmap = SKBitmap.FromImage(snap);
        return bitmap;
    }

    private static int CalcHeight(string? addr, string? cashier,
                                   List<(string, string)> lines, string? note,
                                   float fontScale = 1.0f)
    {
        int lh = (int)(LineH * fontScale);
        // Header cố định
        int h = 16 + LineHLarge + 4;
        if (!string.IsNullOrWhiteSpace(addr)) h += LineH;
        h += 12 + LineH;            // date (cố định)
        // Nội dung scale
        h += 12 + lh + 12;          // separators + header row
        h += lines.Count * lh;
        h += 12 + (int)((LineH + 5) * fontScale) + 10; // total
        h += 12 + lh + 20;          // footer
        return Math.Max(h + 60, 300);
    }

    // ─── Drawing helpers ─────────────────────────────────────────────────────

    private static SKFont BuildFont(float size, bool bold)
    {
        EnsureFonts();
        // Dùng Roboto Mono nếu đã load thành công, fallback về system monospace
        var tf = (bold ? _tfBold : _tfRegular)
                 ?? SKTypeface.FromFamilyName("monospace",
                        bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
                        SKFontStyleWidth.Normal, SKFontStyleSlant.Upright)
                 ?? SKTypeface.Default;
        return new SKFont(tf, size);
    }

    private static SKPaint BuildPaint(bool bold) => new SKPaint
    {
        Color        = SKColors.Black,
        FakeBoldText = bold,
        IsAntialias  = true,   // cần cho Courier New — tránh mất nét mảnh
    };

    private static int LeftText(SKCanvas c, string txt, int y, float size, bool bold = false)
    {
        using var font  = BuildFont(size, bold);
        using var paint = BuildPaint(bold);
        c.DrawText(txt, PadX, y + size, font, paint);
        return y + (int)(size * 1.4f);
    }

    private static int CenterText(SKCanvas c, string txt, int y, float size, bool bold = false)
    {
        using var font  = BuildFont(size, bold);
        using var paint = BuildPaint(bold);
        float w = font.MeasureText(txt);
        float x = Math.Max(PadX, (PrintWidthPx - w) / 2f);
        c.DrawText(txt, x, y + size, font, paint);
        return y + (int)(size * 1.4f);
    }

    private static int TwoCol(SKCanvas c, string left, string right,
                               int y, float size, bool bold = false)
    {
        using var font  = BuildFont(size, bold);
        using var paint = BuildPaint(bold);
        float rw   = font.MeasureText(right);
        float rx   = PrintWidthPx - PadX - rw;
        float maxL = rx - PadX - 8;

        // Cắt chuỗi trái nếu quá dài
        string l = left;
        while (l.Length > 1 && font.MeasureText(l) > maxL)
            l = l[..^1];

        c.DrawText(l, PadX, y + size, font, paint);
        if (!string.IsNullOrWhiteSpace(right))
            c.DrawText(right, rx, y + size, font, paint);
        return y + (int)(size * 1.4f);
    }

    private static int HRule(SKCanvas c, int y, bool doubled = false)
    {
        using var p = new SKPaint { Color = SKColors.Black, StrokeWidth = 1.5f };
        c.DrawLine(PadX, y + 3, PrintWidthPx - PadX, y + 3, p);
        if (doubled) c.DrawLine(PadX, y + 7, PrintWidthPx - PadX, y + 7, p);
        return y + (doubled ? 12 : 7);
    }

    private static int DrawQr(SKCanvas canvas, string content, int y)
    {
        try
        {
            var writer = new ZXing.BarcodeWriterPixelData
            {
                Format  = ZXing.BarcodeFormat.QR_CODE,
                Options = new ZXing.Common.EncodingOptions { Width = 120, Height = 120, Margin = 1 }
            };
            var pd = writer.Write(content);

            using var qrBmp = new SKBitmap(pd.Width, pd.Height);
            for (int py = 0; py < pd.Height; py++)
                for (int px = 0; px < pd.Width; px++)
                {
                    int i = (py * pd.Width + px) * 4;
                    byte r = pd.Pixels[i];
                    qrBmp.SetPixel(px, py, r < 128 ? SKColors.Black : SKColors.White);
                }

            float qx = (PrintWidthPx - pd.Width) / 2f;
            canvas.DrawBitmap(qrBmp, qx, y);
            return y + pd.Height + 6;
        }
        catch { return y; }
    }

    // ─── ESC/POS Raster GS v 0 ───────────────────────────────────────────────

    /// <summary>
    /// Chuyển SKBitmap → lệnh ESC/POS GS v 0 (raster bit image).
    /// Định dạng: GS v 0 m xL xH yL yH [dữ liệu theo hàng, MSB trước]
    /// </summary>
    private static byte[] BitmapToRaster(SKBitmap bmp)
    {
        int width       = bmp.Width;
        int height      = bmp.Height;
        int bytesPerRow = (width + 7) / 8;  // ceil(width/8)

        // Giới hạn số hàng mỗi lần gửi (tránh buffer overflow trên một số máy)
        const int maxRows = 200;
        var buf = new List<byte>();

        for (int startY = 0; startY < height; startY += maxRows)
        {
            int rows = Math.Min(maxRows, height - startY);

            // GS v 0 m xL xH yL yH
            buf.Add(GS);
            buf.Add((byte)'v');
            buf.Add((byte)'0');
            buf.Add(0);                                // m=0: normal size
            buf.Add((byte)(bytesPerRow & 0xFF));       // xL
            buf.Add((byte)(bytesPerRow >> 8));          // xH
            buf.Add((byte)(rows & 0xFF));              // yL
            buf.Add((byte)(rows >> 8));                // yH

            // Dữ liệu: từng hàng từ trái sang phải, MSB = pixel trái nhất
            for (int row = startY; row < startY + rows; row++)
            {
                for (int b = 0; b < bytesPerRow; b++)
                {
                    byte byt = 0;
                    for (int bit = 0; bit < 8; bit++)
                    {
                        int col = b * 8 + bit;
                        if (col < width)
                        {
                            var px = bmp.GetPixel(col, row);
                            // Ngưỡng 200: giữ lại pixel xám từ anti-aliasing → chữ đậm hơn
                            // (128 = chỉ lấy pixel đen đậm; 200 = lấy cả xám nhạt)
                            int brightness = (px.Red * 299 + px.Green * 587 + px.Blue * 114) / 1000;
                            if (brightness < 200)
                                byt |= (byte)(0x80 >> bit);
                        }
                    }
                    buf.Add(byt);
                }
            }
        }

        return [.. buf];
    }

    private static byte[] FeedAndCut(int lines)
    {
        var c = new List<byte>();
        for (int i = 0; i < lines; i++) c.Add(LF);
        c.AddRange([GS, (byte)'V', (byte)'A', (byte)lines]);
        return [.. c];
    }
}
