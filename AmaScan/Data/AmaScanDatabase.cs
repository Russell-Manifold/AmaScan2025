using AmaScan.Classes;
using AmaScan.sqliteModels;
using SQLite;
using System;
using System.Diagnostics;

namespace AmaScan.Data;

public class AmaScanDatabase
{
    private static AmaScanDatabase? _instance;
    private static readonly object _lock = new object();
    private static SQLiteAsyncConnection? _database;
    private static bool _initialized = false;
    private static readonly SemaphoreSlim _initSemaphore = new SemaphoreSlim(1, 1);

    // Private constructor to prevent direct instantiation
    private AmaScanDatabase()
    {
        // Initialize asynchronously without blocking
        _ = InitAsync();
    }

    // Singleton instance
    public static AmaScanDatabase Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = new AmaScanDatabase();
                    }
                }
            }
            return _instance;
        }
    }

    private async Task InitAsync()
    {
        if (_initialized)
            return;

        await _initSemaphore.WaitAsync();
        try
        {
            if (_initialized)
                return;

            _database = new SQLiteAsyncConnection(Constants.DatabasePath, Constants.Flags);
            _initialized = true;
        }
        finally
        {
            _initSemaphore.Release();
        }
    }

    // POCOs to map the results
    public class IndexRow { public string name { get; set; } }
    public class PlanRow { public string detail { get; set; } }

    public async Task<SQLiteAsyncConnection> GetDatabaseAsync()
    {
        if (!_initialized)
            await InitAsync();
        return _database!;
    }

    public static SQLiteAsyncConnection GetConnection()
    {
        if (_database == null)
        {
            lock (_lock)
            {
                if (_database == null)
                {
                    _database = new SQLiteAsyncConnection(Constants.DatabasePath, Constants.Flags);
                }
            }
        }
        return _database;
    }

    // Static method to get a DatabaseHelper instance
    public static async Task<DatabaseHelper> GetDatabaseHelperAsync()
    {
        var instance = Instance;
        var connection = await instance.GetDatabaseAsync();
        return new DatabaseHelper(connection);
    }

    // Static method to get a DatabaseHelper instance synchronously (use with caution)
    public static DatabaseHelper GetDatabaseHelper()
    {
        var connection = GetConnection();
        return new DatabaseHelper(connection);
    }
}
