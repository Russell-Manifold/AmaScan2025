using AmaScan.Classes;
using AmaScan.Data;
using AmaScan.sqliteModels;
using SQLite;

namespace AmaScan
{
    public partial class App : Application
    {
        public static IServiceProvider Services { get; private set; }
        public static DatabaseHelper Db { get; set; }
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

            // Migrate existing PoHeader table with new receiving audit columns
            MigratePoHeaderAsync(connection).ConfigureAwait(false);

            // Instantiate the singleton helper
            Db = new DatabaseHelper(connection);


            MainPage = new AppShell(); // or NavigationPage if needed
        }

        private static async Task MigratePoHeaderAsync(SQLiteAsyncConnection conn)
        {
            var newColumns = new Dictionary<string, string>
            {
                ["Receiver"] = "TEXT",
                ["ReceiveStartTime"] = "TEXT",
                ["ReceiveEndTime"] = "TEXT",
                ["Authorised"] = "TEXT",
                ["GrvNumber"] = "TEXT",
                ["DeviceName"] = "TEXT",
                ["TotalLines"] = "INTEGER DEFAULT 0",
                ["ScannedLines"] = "INTEGER DEFAULT 0",
                ["DiscrepancyLines"] = "INTEGER DEFAULT 0"
            };

            foreach (var kv in newColumns)
            {
                try
                {
                    await conn.ExecuteAsync($"ALTER TABLE PoHeader ADD COLUMN {kv.Key} {kv.Value}");
                }
                catch
                {
                    // Column already exists — skip
                }
            }
        }
    }
}
