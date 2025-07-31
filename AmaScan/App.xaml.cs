using AmaScan.Data;

namespace AmaScan
{
    public partial class App : Application
    {
        public static IServiceProvider Services { get; private set; }

        public App(IServiceProvider serviceProvider)
        {
            InitializeComponent();
            Services = serviceProvider;
            
            // Pre-initialize database on background thread to avoid blocking UI
            _ = Task.Run(async () =>
            {
                try
                {
                    var database = AmaScanDatabase.Instance;
                    await database.GetDatabaseAsync();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Database initialization error: {ex.Message}");
                }
            });
            
            MainPage = new AppShell(); // or NavigationPage if needed
        }
    }
}
