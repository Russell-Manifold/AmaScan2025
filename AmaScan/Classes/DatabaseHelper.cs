using AmaScan.sqliteModels;
using SQLite;
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AmaScan.Classes
{
    public class DatabaseHelper
    {
        private readonly SQLiteAsyncConnection _dbConnection;

        // Constructor assumes the SQLite connection has already been initialized
        public DatabaseHelper(SQLiteAsyncConnection dbConnection)
        {
            _dbConnection = dbConnection ?? throw new ArgumentNullException(nameof(dbConnection));
        }

        // Insert a new item
        public Task<int> InsertAsync<T>(T item)
        {
            return _dbConnection.InsertAsync(item);
        }

        // Update an existing item
        public Task<int> UpdateAsync<T>(T item)
        {
            return _dbConnection.UpdateAsync(item);
        }

        // Get a specific item by ID
        public async Task<T> GetItemAsync<T>(int id) where T : IIdentifiable, new()
        {
            return await _dbConnection.Table<T>()
                                      .Where(i => i.Id == id) // Directly access Id since T implements IIdentifiable
                                      .FirstOrDefaultAsync();
        }

        // Get all items from a table
        public Task<List<T>> GetItemsAsync<T>() where T : new()
        {
            return _dbConnection.Table<T>().ToListAsync();
        }

        // Delete an item
        public Task<int> DeleteAsync<T>(T item)
        {
            return _dbConnection.DeleteAsync(item);
        }

        public async Task<List<PoLine>> GetPoLinesByOrderNoAsync(string orderNo)
        {
            return await _dbConnection.Table<PoLine>()
                .Where(line => line.OrderNo == orderNo)
                .OrderBy(line => line.LineNo)
                .ToListAsync();
        }

        public async Task UpdatePoLineAsync(PoLine poLine)
        {
            await _dbConnection.UpdateAsync(poLine);
        }

        public async Task UpdatePoHeaderAsync(PoHeader poHeader)
        {
            await _dbConnection.UpdateAsync(poHeader);
        }

        public Task<PoHeader> GetPoHeaderByOrderNoAsync(string orderNo)
        {
            return _dbConnection.Table<PoHeader>()
                .FirstOrDefaultAsync(h => h.OrderNo == orderNo);
        }

        public async Task SaveStockItemsAsync(List<StockItem> items)
        {
            await _dbConnection.DeleteAllAsync<StockItem>();
            await _dbConnection.InsertAllAsync(items);
        }

        public async Task<StockItem?> ResolveStockItemByBarcodeAsync(string scannedBarcode)
        {
            var allItems = await _dbConnection.Table<StockItem>().ToListAsync();

            return allItems.FirstOrDefault(item =>
                (!string.IsNullOrWhiteSpace(item.bar_code) && item.bar_code.Equals(scannedBarcode, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(item.barcode_lmmp) && item.barcode_lmmp.Equals(scannedBarcode, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(item.alternate_bar_codes) && item.alternate_bar_codes
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Any(b => b.Equals(scannedBarcode, StringComparison.OrdinalIgnoreCase)))
            );
        }

        public async Task DeletePoAsync(string poNumber)
        {
            var poLines = await _dbConnection.Table<PoLine>().Where(p => p.OrderNo == poNumber).ToListAsync();
            foreach (var line in poLines)
                await _dbConnection.DeleteAsync(line);

            var poHeader = await _dbConnection.Table<PoHeader>().FirstOrDefaultAsync(p => p.OrderNo == poNumber);
            if (poHeader != null)
                await _dbConnection.DeleteAsync(poHeader);
        }

        // Save (replace all) warehouses from API to local DB
        public async Task SaveWarehousesAsync(List<Warehouse> warehouses)
        {
            await _dbConnection.DeleteAllAsync<Warehouse>();
            await _dbConnection.InsertAllAsync(warehouses);
        }

        // Get all warehouses
        public Task<List<Warehouse>> GetWarehousesAsync()
        {
            return _dbConnection.Table<Warehouse>().ToListAsync();
        }

        // Get a warehouse by code
        public Task<Warehouse?> GetWarehouseByCodeAsync(string code)
        {
            return _dbConnection.Table<Warehouse>()
                                .FirstOrDefaultAsync(w => w.Code == code);
        }

        // Get description by code (utility method)
        public async Task<string?> GetWarehouseDescriptionAsync(string code)
        {
            var warehouse = await GetWarehouseByCodeAsync(code);
            return warehouse?.Description;
        }

        // Optional: Delete all warehouses
        public Task<int> ClearWarehousesAsync()
        {
            return _dbConnection.DeleteAllAsync<Warehouse>();
        }

        public async Task<bool> HasWarehousesAsync()
        {
            var count = await _dbConnection.Table<Warehouse>().CountAsync();
            return count > 0;
        }

    }
}
