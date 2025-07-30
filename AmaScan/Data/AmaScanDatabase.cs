using SQLite;
using AmaScan.sqliteModels;


namespace AmaScan.Data;

public class AmaScanDatabase
{
    private SQLiteAsyncConnection Database = null!;
    private static bool _initialized = false;

    public AmaScanDatabase()
    {
        Task.Run(async () => await Init()).Wait();
    }

    private async Task Init()
    {
        if (_initialized)
            return;
        Database = new SQLiteAsyncConnection(Constants.DatabasePath, Constants.Flags);
        await Database.CreateTableAsync<StockItem>();
        await Database.CreateTableAsync<PoHeader>();
        await Database.CreateTableAsync<PoLine>();
        await Database.CreateTableAsync<Warehouse>();
        await Database.CreateTableAsync<SoHeader>();
        await Database.CreateTableAsync<SoLine>();
        await Database.CreateTableAsync<ReturnLine>();
        await Database.CreateTableAsync<StockCountItem>();

        _initialized = true;
    }
}
