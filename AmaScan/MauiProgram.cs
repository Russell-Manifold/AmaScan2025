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
            builder.Services.AddSingleton<AmaScanDatabase>();
            builder.Services.AddSingleton<UserSession>();
            builder.Services.AddTransient<Dashboard>();
            builder.Services.AddTransient<DashboardPicking>();
            builder.Services.AddSingleton<MainPage>();
            builder.Services.AddSingleton<ReceivingMain>();
            builder.Services.AddTransient<ReceivingDocumentsPage>();
            builder.Services.AddSingleton<ReceivingPage>();
            builder.Services.AddSingleton<SettingsPage>();
            builder.Services.AddTransient<TransferMainPage>();
            builder.Services.AddTransient<TransferDetailPage>();
            builder.Services.AddTransient<PickingMain>();
            builder.Services.AddTransient<PickingDocumentsPage>();
            builder.Services.AddTransient<PickingPage>();
            builder.Services.AddTransient<PackingMain>();
            builder.Services.AddTransient<PackingDocumentsPage>();
            builder.Services.AddTransient<PackingPage>();
            builder.Services.AddTransient<CheckingMain>();
            builder.Services.AddTransient<CheckingDocumentsPage>();
            builder.Services.AddTransient<CheckingPage>();
            builder.Services.AddTransient<AuthorizationMain>();
            builder.Services.AddTransient<AuthorizationDocumentsPage>();
            builder.Services.AddTransient<AuthorizationPage>();

            return builder.Build();
        }
    }
}
