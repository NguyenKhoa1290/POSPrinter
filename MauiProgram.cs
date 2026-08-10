using Microsoft.Extensions.Logging;
using POSPrinter.Services;
using POSPrinter.ViewModels;
using POSPrinter.Views;
using ZXing.Net.Maui.Controls;

namespace POSPrinter;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
#if IOS || ANDROID
            .UseBarcodeReader()   // ZXing chỉ hỗ trợ iOS/Android, không hỗ trợ Windows
#endif
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // ── Register Services ──────────────────────────────────────────────

#if IOS
        builder.Services.AddSingleton<IBluetoothPrinterService,
            POSPrinter.Platforms.iOS.IosBleBluetoothPrinterService>();
#elif ANDROID
        builder.Services.AddSingleton<IBluetoothPrinterService,
            POSPrinter.Platforms.Android.AndroidBluetoothPrinterService>();
#elif WINDOWS
        builder.Services.AddSingleton<IBluetoothPrinterService,
            POSPrinter.Platforms.Windows.WindowsBluetoothPrinterService>();
#else
        builder.Services.AddSingleton<IBluetoothPrinterService,
            StubBluetoothPrinterService>();
#endif

        // Lịch sử hóa đơn — lưu cục bộ + đồng bộ Firebase Realtime Database
        builder.Services.AddSingleton<InvoiceHistoryService>();

        // ── Register ViewModels & Pages ────────────────────────────────────
        builder.Services.AddSingleton<InvoiceViewModel>();
        builder.Services.AddSingleton<InvoicePage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
