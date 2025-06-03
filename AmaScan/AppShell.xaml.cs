namespace AmaScan
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            Routing.RegisterRoute(nameof(ReceivingMain), typeof(ReceivingMain));
            Routing.RegisterRoute(nameof(ReceivingPage), typeof(ReceivingPage));
           
            Routing.RegisterRoute(nameof(ReceivingDocumentsPage), typeof(ReceivingDocumentsPage));

            Routing.RegisterRoute(nameof(TransferMainPage), typeof(TransferMainPage));
            Routing.RegisterRoute(nameof(TransferDetailPage), typeof(TransferDetailPage));

            Routing.RegisterRoute(nameof(SettingsPage), typeof(SettingsPage));
            InitializeComponent();
            
        }
    }
}
