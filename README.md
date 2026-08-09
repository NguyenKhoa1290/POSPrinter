# POSPrinter — .NET MAUI

Ung dung in hoa don nhiet **58mm** qua **Bluetooth** (iOS BLE / Android SPP).

## Cau truc du an

```
POSPrinter/
├── Models/             Invoice.cs, InvoiceItem.cs, BluetoothDevice.cs
├── Services/           IBluetoothPrinterService.cs, EscPosBuilder.cs
├── Platforms/iOS/      IosBleBluetoothPrinterService.cs, Info.plist, Entitlements.plist
├── Platforms/Android/  AndroidBluetoothPrinterService.cs, AndroidManifest.xml
├── ViewModels/         InvoiceViewModel.cs
├── Views/              InvoicePage.xaml
├── Converters/         Converters.cs
└── MauiProgram.cs
```

## Tinh nang

- Quet Bluetooth va ket noi may in nhiet
- Nhap lieu hoa don (san pham, so luong, don gia)
- Xem truoc hoa don 58mm trong app
- In ESC/POS voi QR Code va tieng Viet (CP1258)
- Giam gia, thue, ghi chu footer

## Build

```bash
# iOS
dotnet build -f net9.0-ios

# Android
dotnet build -f net9.0-android
```

## Yeu cau may in

**iOS (BLE)**: May in ho tro BLE nhu Xprinter XP-58BH, GOOJPRT PT-210, iDPRT SP410
**Android (SPP)**: Bat ky may in nhiet Bluetooth Classic nao (SPP UUID: 00001101)

## Cau hinh may in iOS

App tu dong tim kiem cac Service UUID pho bien:
- E7810A71 (Xprinter)
- 49535343 (Generic BLE Serial)
- 0000FF00 (iDPRT/GOOJPRT)
- 0000FFF0

## Doi kho giay

Trong EscPosBuilder.cs:
- 58mm = 32 chars/dong (mac dinh)
- 80mm = doi thanh 48 chars/dong
