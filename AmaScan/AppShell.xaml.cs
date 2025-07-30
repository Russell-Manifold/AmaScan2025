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

            Routing.RegisterRoute(nameof(PickingMain), typeof(PickingMain));
            Routing.RegisterRoute(nameof(PickingPage), typeof(PickingPage));
            Routing.RegisterRoute(nameof(PickingDocumentsPage), typeof(PickingDocumentsPage));
            
            Routing.RegisterRoute(nameof(PackingMain), typeof(PackingMain));
            Routing.RegisterRoute(nameof(PackingPage), typeof(PackingPage));
            Routing.RegisterRoute(nameof(PackingDocumentsPage), typeof(PackingDocumentsPage));

            Routing.RegisterRoute(nameof(CheckingMain), typeof(CheckingMain));
            Routing.RegisterRoute(nameof(CheckingPage), typeof(CheckingPage));
            Routing.RegisterRoute(nameof(CheckingDocumentsPage), typeof(CheckingDocumentsPage));

            Routing.RegisterRoute(nameof(AuthorizationMain), typeof(AuthorizationMain));
            Routing.RegisterRoute(nameof(AuthorizationPage), typeof(AuthorizationPage));
            Routing.RegisterRoute(nameof(AuthorizationDocumentsPage), typeof(AuthorizationDocumentsPage));

            Routing.RegisterRoute(nameof(StockCountMain), typeof(StockCountMain));
            Routing.RegisterRoute(nameof(StockCountPage), typeof(StockCountPage));
            Routing.RegisterRoute(nameof(StockCountSearchPage), typeof(StockCountSearchPage));

            InitializeComponent();
            
        }
    }
}
