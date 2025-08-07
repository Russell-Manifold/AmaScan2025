using AmaScan.Classes;
using AmaScan.Data;
using AmaScan.sqliteModels;
using SQLite;

namespace AmaScan
{
    public partial class App : Application
    {
        public static IServiceProvider Services { get; private set; }

        public static DatabaseHelper Db { get; private set; }
        public App(IServiceProvider serviceProvider)
        {
            InitializeComponent();
            Services = serviceProvider;

            var dbPath = Path.Combine(FileSystem.AppDataDirectory, "amascan.db3");
            var connection = new SQLiteAsyncConnection(dbPath);

            // Create tables before the helper is handed out
            _ = connection.CreateTablesAsync(
                    CreateFlags.None,
                    typeof(StockItem),
                    typeof(PoHeader),
                    typeof(PoLine),
                    typeof(Warehouse),
                    typeof(SoHeader),
                    typeof(SoLine),
                    typeof(ReturnLine),
                    typeof(StockCountItem));

            // Instantiate the singleton helper
            Db = new DatabaseHelper(connection);


            MainPage = new AppShell(); // or NavigationPage if needed
        }
    }
}
