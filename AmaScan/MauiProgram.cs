using AmaScan.Data;
using AmaScan.Classes;

namespace AmaScan
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                // ✅ Pass the service provider to App's constructor
                .UseMauiApp(serviceProvider => new App(serviceProvider))
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

            // Custom entry handler (keep as-is if needed)
            EntryHandlerMapper.Configure();

            // ✅ Register your services
            builder.Services.AddSingleton<AmaScanDatabase>(provider => AmaScanDatabase.Instance);
            builder.Services.AddSingleton<UserSession>();
            
            // Register DatabaseHelper factory
            builder.Services.AddTransient<DatabaseHelper>(provider =>
            {
                return AmaScanDatabase.GetDatabaseHelper();
            });
            builder.Services.AddSingleton<MainPage>();
            builder.Services.AddSingleton<ReceivingMain>();
            builder.Services.AddSingleton<TransferMainPage>();
            builder.Services.AddSingleton<PickingMain>();
            builder.Services.AddSingleton<PackingMain>();
            builder.Services.AddSingleton<CheckingMain>();
            builder.Services.AddSingleton<AuthorizationMain>();
            builder.Services.AddSingleton<StockCountMain>();

            // every page you navigate to with a parameter
            builder.Services.AddTransient<ReceivingDocumentsPage>();
            builder.Services.AddTransient<ReceivingPage>();
            builder.Services.AddTransient<SettingsPage>();
            builder.Services.AddTransient<Dashboard>();
            builder.Services.AddTransient<DashboardPicking>();
            builder.Services.AddTransient<TransferDetailPage>();
            builder.Services.AddTransient<PickingDocumentsPage>();
            builder.Services.AddTransient<PickingPage>();
            builder.Services.AddTransient<PackingDocumentsPage>();
            builder.Services.AddTransient<PackingPage>();
            builder.Services.AddTransient<CheckingDocumentsPage>();
            builder.Services.AddTransient<CheckingPage>();
            builder.Services.AddTransient<AuthorizationDocumentsPage>();
            builder.Services.AddTransient<AuthorizationPage>();
            builder.Services.AddTransient<ReturnsPage>();
            builder.Services.AddTransient<StockCountPage>();
            builder.Services.AddTransient<StockCountSearchPage>();

            return builder.Build();
        }
    }
}
