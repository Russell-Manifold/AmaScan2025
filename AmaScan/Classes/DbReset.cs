using AmaScan.sqliteModels;
using SQLite;

namespace AmaScan.Classes
{
    public static class DbReset
    {
        // 1. Absolute path inside the *writeable* app directory
        public static async Task ResetAsync()
        {
            var connection = App.Db.Connection;

            // 1. List of tables to drop (keep StockItem out)
            var tablesToDrop = new[]
            {
                nameof(PoHeader),
                nameof(PoLine),
                nameof(SoHeader),
                nameof(SoLine),
                nameof(ReturnLine),
                nameof(StockCountItem)
    };

            // 2. Build & execute DROP TABLE ... IF EXISTS
            var dropSql = string.Join(";", tablesToDrop.Select(t => $"DROP TABLE IF EXISTS [{t}]"));
            await connection.ExecuteAsync(dropSql);

            await connection.CreateTablesAsync(
                CreateFlags.None,
                typeof(PoHeader),
                typeof(PoLine),
                typeof(SoHeader),
                typeof(SoLine),
                typeof(ReturnLine),
                typeof(StockCountItem));

            App.Db = new DatabaseHelper(connection);
        }
        public static async Task fullResetAsync()
        {
            var dbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "amascan.db3");

            // Close the connection
            await (App.Db?.Connection?.CloseAsync() ?? Task.CompletedTask);

            // Wait a moment for the OS to release the file
            await Task.Delay(150);

            if (File.Exists(dbPath))
                File.Delete(dbPath);

            var connection = new SQLiteAsyncConnection(
                dbPath,
                SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);

            await connection.CreateTablesAsync(
                CreateFlags.None,
                typeof(StockItem),
                typeof(PoHeader),
                typeof(PoLine),
                typeof(Warehouse),
                typeof(SoHeader),
                typeof(SoLine),
                typeof(ReturnLine),
                typeof(StockCountItem));

            App.Db = new DatabaseHelper(connection);


        }

    }
}
