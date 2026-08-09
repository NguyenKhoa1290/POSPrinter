using POSPrinter.ViewModels;

namespace POSPrinter.Views;

public partial class InvoicePage : ContentPage
{
    public InvoicePage(InvoiceViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
