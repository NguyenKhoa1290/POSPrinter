using POSPrinter.ViewModels;
using POSPrinter.Views;

namespace POSPrinter;

public partial class App : Application
{
    private readonly InvoicePage _invoicePage;
    private readonly InvoiceViewModel _viewModel;

    public App(InvoicePage invoicePage, InvoiceViewModel viewModel)
    {
        InitializeComponent();
        _invoicePage = invoicePage;
        _viewModel   = viewModel;
    }

    /// <summary>
    /// MAUI 9: dùng CreateWindow thay vì MainPage (tránh warning CS0618)
    /// </summary>
    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new NavigationPage(_invoicePage)
        {
            BarBackgroundColor = Color.FromArgb("#1A1A2E"),
            BarTextColor       = Colors.White
        });

        // Về màn hình chính rồi quay lại → hệ điều hành đã ngắt kết nối máy in.
        // Nối lại ngay để lần bấm In kế tiếp không bị chặn.
        window.Resumed += (_, _) => _ = _viewModel.OnResumeAsync();

        return window;
    }
}