using System.Text;
using POSPrinter.Models;

namespace POSPrinter.Services;

/// <summary>Chế độ encoding cho máy in nhiệt POS</summary>
public enum PrinterEncoding
{
    /// <summary>Bỏ dấu → ASCII thuần. Hoạt động với 100% máy in.</summary>
    AsciiNoDiacritics = 0,
    /// <summary>UTF-8 thô (không BOM). Máy in Xprinter/HPRT mới hỗ trợ.</summary>
    Utf8Raw = 1,
    /// <summary>Windows-1258 + ESC t 33. ★ EPSON chính hãng và tương thích.</summary>
    Cp1258_Page33 = 2,
    /// <summary>Windows-1258 + ESC t 30. Máy in có ROM Vietnamese cũ.</summary>
    Cp1258_Page30 = 3,
    /// <summary>Windows-1258 + ESC t 16. Một số firmware khác.</summary>
    Cp1258_Page16 = 4,
    /// <summary>Windows-1258 + ESC t 6. Một số firmware khác.</summary>
    Cp1258_Page6 = 5,
    /// <summary>Windows-1258 không gửỉ ESC t (máy tự cấu hình sẵn).</summary>
    Cp1258_NoCmd = 6,
}

/// <summary>
/// Tạo lệnh ESC/POS cho máy in nhiệt 58mm.
/// Thử lần lượt PrinterEncoding cho đến khi tiếng Việt hiển thị đúng.
/// </summary>
public static class EscPosBuilder
{
    // ─── Cấu hình có thể đổi runtime ────────────────────────────────────────
    public static PrinterEncoding CurrentEncoding { get; set; } =
        PrinterEncoding.Cp1258_Page33; // Mặc định: EPSON ESC t 33 = CP1258 Vietnamese

    // ─── ESC/POS Constants ──────────────────────────────────────────────────
    private const byte ESC = 0x1B;
    private const byte GS  = 0x1D;
    private const byte LF  = 0x0A;
    private const int  COL = 32;   // 58mm = 32 chars/dòng

    // ─── Encoding setup ──────────────────────────────────────────────────────
    private static Encoding? _cp1258;
    private static Encoding Cp1258
    {
        get
        {
            if (_cp1258 != null) return _cp1258;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            try   { _cp1258 = Encoding.GetEncoding(1258); }
            catch { _cp1258 = Encoding.ASCII; }
            return _cp1258;
        }
    }

    // ─── Bảng chuyển Việt → ASCII ────────────────────────────────────────────
    private static readonly (string From, string To)[] VietMap =
    [
        ("àáâãăặắằẳẵấầẩẫậ", "a"), ("ÀÁÂÃĂẶẮẰẲẴẤẦẨẪẬ", "A"),
        ("èéêềếểễệ",          "e"), ("ÈÉÊỀẾỂỄỆ",          "E"),
        ("ìíỉịĩ",             "i"), ("ÌÍỈỊĨ",             "I"),
        ("òóôõồốổỗộờớởỡợ",   "o"), ("ÒÓÔÕỒỐỔỖỘỜỚỞỠỢ",   "O"),
        ("ùúủụũừứửữự",        "u"), ("ÙÚỦỤŨỪỨỬỮỰ",        "U"),
        ("ỳýỷỹỵ",             "y"), ("ỲÝỶỸỴ",             "Y"),
        ("đ",                  "d"), ("Đ",                  "D"),
    ];

    public static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            bool found = false;
            foreach (var (from, to) in VietMap)
                if (from.Contains(c)) { sb.Append(to); found = true; break; }
            if (!found) sb.Append(c);
        }
        return sb.ToString();
    }

    // ─── Public API ──────────────────────────────────────────────────────────

    public static byte[] BuildInvoice(Invoice invoice)
    {
        var buf = new List<byte>();

        buf.AddRange(Initialize());

        // Header
        buf.AddRange(SetAlign(Align.Center));
        buf.AddRange(SetBold(true));
        buf.AddRange(SetFontSize(2, 2));
        buf.AddRange(Enc(invoice.StoreName.ToUpper()));
        buf.Add(LF);
        buf.AddRange(SetFontSize(1, 1));
        buf.AddRange(SetBold(false));

        if (!string.IsNullOrWhiteSpace(invoice.StoreAddress))
        { buf.AddRange(Enc(invoice.StoreAddress)); buf.Add(LF); }

        buf.AddRange(Line('='));
        buf.AddRange(SetAlign(Align.Left));
        buf.AddRange(Enc($"Ngày : {invoice.CreatedAt:dd/MM/yyyy HH:mm}")); buf.Add(LF);

        if (!string.IsNullOrWhiteSpace(invoice.Cashier))


        buf.AddRange(Line('-'));
        buf.AddRange(SetBold(true));
        buf.AddRange(Enc(ColHeader())); buf.Add(LF);
        buf.AddRange(SetBold(false));
        buf.AddRange(Line('-'));

        foreach (var item in invoice.Items)
        {
            var nameLines = WrapText(item.Name, 18);
            for (int i = 0; i < nameLines.Count; i++)
            {
                string row = i == 0
                    ? FormatItemLine(nameLines[i], item.Quantity, item.UnitPrice, item.TotalPrice)
                    : $"  {nameLines[i]}";
                buf.AddRange(Enc(row)); buf.Add(LF);
            }
        }

        buf.AddRange(Line('='));
        buf.AddRange(SetAlign(Align.Right));
        buf.AddRange(SetBold(true));
        buf.AddRange(SetFontSize(1, 2));
        buf.AddRange(Enc($"TONG CONG: {MoneyFull(invoice.GrandTotal)}")); buf.Add(LF);
        buf.AddRange(SetFontSize(1, 1));
        buf.AddRange(SetBold(false));

        buf.AddRange(SetAlign(Align.Center));
        buf.AddRange(Line('-'));
        string qr = $"HD:{invoice.InvoiceNumber}|T:{invoice.GrandTotal:F0}|D:{invoice.CreatedAt:yyyyMMddHHmm}";
        buf.AddRange(PrintQRCode(qr, 4));

        buf.Add(LF);
        string note = !string.IsNullOrWhiteSpace(invoice.Note) ? invoice.Note : "Cam on quy khach!";
        buf.AddRange(Enc(note)); buf.Add(LF);
        buf.AddRange(FeedAndCut(5));

        return [.. buf];
    }

    // ─── ESC/POS Commands ────────────────────────────────────────────────────

    private static byte[] Initialize()
    {
        var cmd = new List<byte> { ESC, (byte)'@' }; // Reset
        switch (CurrentEncoding)
        {
            case PrinterEncoding.Cp1258_Page33: cmd.AddRange([ESC, (byte)'t', 33]); break;
            case PrinterEncoding.Cp1258_Page30: cmd.AddRange([ESC, (byte)'t', 30]); break;
            case PrinterEncoding.Cp1258_Page16: cmd.AddRange([ESC, (byte)'t', 16]); break;
            case PrinterEncoding.Cp1258_Page6:  cmd.AddRange([ESC, (byte)'t',  6]); break;
            case PrinterEncoding.Utf8Raw:        cmd.AddRange([ESC, (byte)'t',  0]); break;
            default: break; // ASCII / Cp1258_NoCmd: không gửi ESC t
        }
        return [.. cmd];
    }

    private static byte[] Enc(string text)
    {
        switch (CurrentEncoding)
        {
            case PrinterEncoding.AsciiNoDiacritics:
                return Encoding.ASCII.GetBytes(Normalize(text));

            case PrinterEncoding.Utf8Raw:
                return Encoding.UTF8.GetBytes(text);

            case PrinterEncoding.Cp1258_Page33:
            case PrinterEncoding.Cp1258_Page30:
            case PrinterEncoding.Cp1258_Page16:
            case PrinterEncoding.Cp1258_Page6:
            case PrinterEncoding.Cp1258_NoCmd:
                try   { return Cp1258.GetBytes(text); }
                catch { return Encoding.ASCII.GetBytes(Normalize(text)); }

            default:
                return Encoding.ASCII.GetBytes(Normalize(text));
        }
    }

    private static byte[] SetAlign(Align a)  => [ESC, (byte)'a', (byte)a];
    private static byte[] SetBold(bool b)    => [ESC, (byte)'E', (byte)(b ? 1 : 0)];
    private static byte[] SetFontSize(int w, int h)
    {
        byte n = (byte)((Math.Clamp(w-1,0,7) << 4) | Math.Clamp(h-1,0,7));
        return [GS, (byte)'!', n];
    }
    private static byte[] FeedAndCut(int lines)
    {
        var c = new List<byte>();
        for (int i = 0; i < lines; i++) c.Add(LF);
        c.AddRange([GS, (byte)'V', (byte)'A', (byte)lines]);
        return [.. c];
    }
    private static byte[] PrintQRCode(string content, int qrSize = 4)
    {
        var c = new List<byte>();
        byte[] data = Encoding.ASCII.GetBytes(content);
        c.AddRange([GS,(byte)'(',(byte)'k',4,0,49,65,50,0]);
        c.AddRange([GS,(byte)'(',(byte)'k',3,0,49,67,(byte)Math.Clamp(qrSize,1,8)]);
        c.AddRange([GS,(byte)'(',(byte)'k',3,0,49,69,50]);
        int len = data.Length + 3;
        c.AddRange([GS,(byte)'(',(byte)'k',(byte)(len&0xFF),(byte)(len>>8),49,80,48]);
        c.AddRange(data);
        c.AddRange([GS,(byte)'(',(byte)'k',3,0,49,81,48]);
        return [.. c];
    }

    // ─── Text Helpers ─────────────────────────────────────────────────────────

    private static byte[] Line(char ch) => Enc(new string(ch, COL) + "\n");
    private static string ColHeader()   => PadR("TEN SAN PHAM",18)+PadL("SL",2)+PadL("DON GIA",7)+PadL("TONG",5);
    private static string FormatItemLine(string name, int qty, decimal up, decimal tp)
        => PadR(name,18)+PadL(qty.ToString(),2)+PadL(MoneyShort(up),7)+PadL(MoneyShort(tp),5);
    private static string MoneyFull(decimal v)  => v.ToString("N0").PadLeft(10)+" VND";
    private static string MoneyShort(decimal v) =>
        v>=1_000_000?$"{v/1000:0}K":v>=1_000?$"{v/1000:0.#}K":$"{v:0}";
    private static List<string> WrapText(string text, int maxWidth)
    {
        var lines = new List<string>();
        while (text.Length > maxWidth)
        {
            int b = text.LastIndexOf(' ', maxWidth); if (b<=0) b=maxWidth;
            lines.Add(text[..b].Trim()); text = text[b..].Trim();
        }
        lines.Add(text); return lines;
    }
    private static string PadR(string s,int w) => s.Length>=w?s[..w]:s.PadRight(w);
    private static string PadL(string s,int w) => s.Length>=w?s[^w..]:s.PadLeft(w);
}

public enum Align : byte { Left=0, Center=1, Right=2 }
