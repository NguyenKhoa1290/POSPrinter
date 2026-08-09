using POSPrinter.Views;

namespace POSPrinter;

public partial class App : Application
{
    private readonly InvoicePage _invoicePage;

    public App(InvoicePage invoicePage)
    {
        InitializeComponent();
        _invoicePage = invoicePage;
    }

    /// <summary>
    /// MAUI 9: dùng CreateWindow thay vì MainPage (tránh warning CS0618)
    /// </summary>
    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new NavigationPage(_invoicePage)
        {
            BarBackgroundColor = Color.FromArgb("#1A1A2E"),
            BarTextColor       = Colors.White
        });
    }
}