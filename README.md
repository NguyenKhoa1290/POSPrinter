# POS Printer

Ứng dụng in hóa đơn nhiệt 58mm qua Bluetooth. Chạy trên **iOS, Android và Windows** từ cùng một codebase .NET MAUI.

Nghe thì đơn giản — gõ vài dòng chữ rồi bắn sang máy in. Thực tế thì mỗi nền tảng lại có một cách nghĩ khác nhau về Bluetooth, còn máy in nhiệt giá rẻ thì có cách nghĩ riêng về tiếng Việt. Phần lớn code trong repo này sinh ra để hòa giải ba bên đó.

---

## Nhìn thử

![Windows, iPhone và Android cùng chạy một bản POS Printer](Introduced%20image/photo_2026-08-10_16-56-18.jpg)

Một codebase, ba máy, không có dòng giao diện nào viết riêng cho nền tảng nào. Bên trái là bản Windows đang chạy debug trong Visual Studio, giữa là iPhone, phải là Android — hai máy sau đều đã bắt được máy in `RPP02N` (chấm xanh ở góc trên), còn bản Windows thì chưa kết nối nên khung Bluetooth vẫn mở sẵn chờ quét.

Ảnh cũng cho thấy mấy chi tiết mà phần dưới sẽ nói kỹ: khung thu gọn được và **mỗi máy nhớ trạng thái riêng** (Android đang mở THÔNG TIN CỬA HÀNG, iPhone thì đóng), ô nhập hóa đơn hai cột tên/giá, và mục ENCODING MÁY IN dành cho lúc chữ tiếng Việt in ra bị loạn.

**[Video demo thao tác trong app](Introduced%20image/video_2026-08-10_16-57-11.mp4)** *(mp4, ~20 MB — bấm để tải về xem)*

---

## Ba câu chuyện đằng sau codebase

### 1. Cùng một máy in, hai giao thức hoàn toàn khác nhau

Máy in nhiệt Bluetooth gần như luôn nói **SPP** (Serial Port Profile) trên Bluetooth Classic. Android nói chuyện với nó thoải mái: mở RFCOMM socket tới UUID `00001101-0000-1000-8000-00805F9B34FB`, ghi byte vào stream, xong.

iOS thì không. Apple **không cho app truy cập Bluetooth Classic** trừ khi thiết bị có chứng nhận MFi — mà máy in 300 nghìn ngoài chợ thì đương nhiên không có. Đường duy nhất còn lại là **BLE qua CoreBluetooth**: quét quảng bá, dò service, dò characteristic ghi được, rồi bắn dữ liệu thành từng gói 20 byte.

Kết quả là hai implementation chẳng liên quan gì nhau, chung một interface:

| | Android | iOS | Windows |
|---|---|---|---|
| Giao thức | BT Classic SPP | BLE (CoreBluetooth) | BT Classic |
| Tìm thiết bị | Đọc danh sách đã ghép đôi | Quét quảng bá BLE | Đọc thiết bị hệ thống |
| Địa chỉ | MAC | UUID do iOS cấp | MAC |
| Gửi dữ liệu | Ghi thẳng vào stream | Chia gói 20 byte |  Ghi thẳng vào stream |

Cái bảng này giải thích vì sao "sửa cho iOS" và "sửa cho Android" trong dự án này hầu như không bao giờ là cùng một việc.

### 2. Tiếng Việt là kẻ thù của máy in nhiệt

Máy in nhiệt được thiết kế cho bảng mã 1 byte. Đưa chữ "Cà phê sữa đá" vào, nhiều máy sẽ nhả ra một mớ ký tự Bắc Âu.

App giải quyết bằng cách **không gửi chữ đi nữa**. `BitmapPrinter` dùng SkiaSharp vẽ toàn bộ hóa đơn thành ảnh đen trắng rộng đúng 384 pixel (58mm ≈ 8 dots/mm × 48mm vùng in), rồi gửi ảnh đó dưới dạng ESC/POS raster. Máy in không cần biết tiếng Việt là gì — nó chỉ việc in ra đúng những chấm đen được bảo.

Đổi lại vẫn còn `EscPosBuilder` với **7 chế độ encoding** để bạn thử nếu muốn in bằng font gốc của máy (nhanh hơn, sắc nét hơn): CP1258 với `ESC t` ở các trang mã 33 / 30 / 16 / 6, UTF-8 thô, và phương án cuối cùng là bỏ dấu hết cho lành.

### 3. Máy in rất hay quên

Đây là phần tốn công nhất, và không có gì trong đó là thuật toán khó — chỉ toàn những chi tiết vòng đời mà nếu bỏ sót một cái là người dùng phải kết nối tay:

- Mở app lên → tự nối lại máy in đã lưu lần trước.
- Trên iOS, lấy lại peripheral bằng UUID đã lưu, **không cần quét** — vì máy in không phải lúc nào cũng quảng bá.
- Về màn hình chính rồi quay lại → hệ điều hành đã ngắt kết nối, app tự nối lại khi `Window.Resumed`.
- Bấm In mà kết nối đã rớt → tự nối lại rồi in, thay vì báo "chưa kết nối" rồi thôi.
- Gửi lệnh thất bại → dọn trạng thái để lần bấm sau nối lại, nhưng **không tự in lại** (giấy có thể đã ra một phần, in lại tự động là ra hóa đơn trùng).

---

## Tính năng

- Soạn hóa đơn kiểu hai cột: tên hàng một bên, giá một bên, tự cộng tổng
- Xem trước bản in 58mm ngay trong app
- Chỉnh cỡ chữ (0.7× → 1.5×) cho hợp từng máy in
- Lưu thông tin cửa hàng, thu ngân, ghi chú footer
- Tự kết nối lại máy in đã dùng lần trước
- **Lịch sử hóa đơn**: xem lại, mở chi tiết từng đơn, xóa đơn
- Các khung giao diện thu gọn được và nhớ trạng thái qua các lần mở app

## Lịch sử hóa đơn — local-first

Máy POS hay rớt wifi, nên thứ tự ghi là **máy trước, cloud sau**:

```
In xong ──▶ ghi vào invoice_history.json (luôn thành công)
              │
              └──▶ PUT lên Firebase Realtime Database
                     │
                     ├─ được  → đánh dấu đã đồng bộ
                     └─ hỏng  → để nguyên, đẩy lại ở lần mở app sau
```

Mất mạng vẫn xem được lịch sử và không mất hóa đơn nào. Khóa trên Firebase có dạng `{ticks:D19}-{guid}` để thứ tự khóa trùng thứ tự thời gian — nhờ đó truy vấn `orderBy="$key"&limitToLast=50` lấy đúng 50 hóa đơn mới nhất mà không cần khai báo index trong security rules.

Xóa một hóa đơn thì **xóa trên Firebase trước**, thành công mới xóa bản trong máy. Làm ngược lại thì hóa đơn sẽ sống lại ở lần tải danh sách kế tiếp.

### Cấu hình

Sửa `Services/FirebaseConfig.cs`:

```csharp
public const string DefaultDatabaseUrl = "https://ten-project-default-rtdb.firebaseio.com";
public const string DefaultAuthSecret  = "";   // để trống nếu rules mở
```

Để trống `DefaultDatabaseUrl` thì app vẫn chạy bình thường, chỉ là lịch sử nằm lại trong máy.

> **Lưu ý bảo mật**: database secret cấp quyền admin toàn bộ database và sẽ nằm trong file binary phát hành. Nếu app đến tay người ngoài, hãy để trống secret và siết security rules thay vì tin vào nó.

---

## Cấu trúc

```
POSPrinter/
├── Models/
│   ├── BluetoothDevice.cs      Thiết bị tìm thấy (bọc chung native device)
│   └── InvoiceRecord.cs        Hóa đơn đã in + các dòng hàng
├── Services/
│   ├── IBluetoothPrinterService.cs   Interface chung cho 3 nền tảng
│   ├── BitmapPrinter.cs             Vẽ hóa đơn → bitmap → ESC/POS raster
│   ├── EscPosBuilder.cs             Đường in bằng font gốc (7 encoding)
│   ├── InvoiceHistoryService.cs     Lưu cục bộ + đồng bộ Firebase
│   ├── FirebaseConfig.cs            ← điền URL database ở đây
│   └── AppPreferences.cs            Cài đặt lưu qua các lần mở app
├── Platforms/
│   ├── iOS/       IosBleBluetoothPrinterService.cs, Info.plist
│   ├── Android/   AndroidBluetoothPrinterService.cs, AndroidManifest.xml
│   └── Windows/   WindowsBluetoothPrinterService.cs
├── ViewModels/    InvoiceViewModel.cs      (gần như toàn bộ logic ở đây)
└── Views/         InvoicePage.xaml         (một trang duy nhất)
```

`Models/Invoice.cs`, `Models/InvoiceItem.cs` và `EscPosBuilder.BuildInvoice()` là tàn dư của bản đầu, hiện không nằm trên đường in nào. Giữ lại để tham khảo cấu trúc lệnh ESC/POS.

---

## Build

```bash
dotnet build -f net9.0-ios
dotnet build -f net9.0-android
dotnet build -f net9.0-windows10.0.19041.0
```

Bản iOS được GitHub Actions build tự động mỗi lần push lên `main` (`.github/workflows/ios.yml`), cho ra **IPA chưa ký** — tải artifact về rồi tự ký bằng 3uTools hoặc AltStore. Không cần máy Mac.

## Máy in tương thích

**Android / Windows** — bất kỳ máy in nhiệt Bluetooth Classic nào có SPP.

**iOS** — phải là máy có BLE. App tự dò các service UUID phổ biến:

| UUID | Dòng máy |
|---|---|
| `E7810A71…` | Xprinter |
| `49535343…` | BLE Serial phổ thông |
| `0000FF00…` | iDPRT, GOOJPRT |
| `0000FFF0…` | biến thể khác |

Không khớp cái nào cũng không sao — app quét toàn bộ service rồi lấy characteristic đầu tiên ghi được.

Máy đã chạy thật: **RPP02N** (xem ảnh đầu trang, kết nối được trên cả iOS lẫn Android). Ngoài ra còn Xprinter XP-58BH, GOOJPRT PT-210, iDPRT SP410.

## Khi có trục trặc

| Hiện tượng | Nhiều khả năng là |
|---|---|
| Chữ in ra loạn xạ | Đổi lựa chọn trong mục ENCODING MÁY IN, hoặc giữ đường bitmap mặc định |
| iOS không thấy máy in | Máy in chỉ có BT Classic, không có BLE — iOS bó tay, không phải lỗi app |
| Android không thấy máy in | Chưa ghép đôi trong Cài đặt hệ thống (Android chỉ liệt kê thiết bị đã ghép) |
| Bấm In không phản ứng | Kết nối đã rớt — app sẽ tự nối lại, chờ 1–3 giây |
| Lịch sử trống dù đã in | Chưa điền `DefaultDatabaseUrl`, hoặc mất mạng (hóa đơn vẫn nằm trong máy) |
| In ra một phần rồi dừng | Giấy sắp hết, hoặc pin máy in yếu |

## Đổi khổ giấy 80mm

- `BitmapPrinter.PrintWidthPx`: `384` → `576`
- `EscPosBuilder`: 32 → 48 ký tự/dòng

---

**Stack**: .NET 9 · MAUI · CommunityToolkit.Mvvm · SkiaSharp · Firebase Realtime Database (REST)
