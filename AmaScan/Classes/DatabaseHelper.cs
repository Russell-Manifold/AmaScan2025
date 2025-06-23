using AmaScan.sqliteModels;
using SQLite;

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

        // Sales Order Operations
        public async Task<List<SoLine>> GetSoLinesByOrderNoAsync(string orderNo)
        {
            return await _dbConnection.Table<SoLine>()
                .Where(line => line.DocNum == orderNo)
                .OrderBy(line => line.Id)
                .ToListAsync();
        }

        public async Task UpdateSoLineAsync(SoLine soLine)
        {
            await _dbConnection.UpdateAsync(soLine);
        }

        public async Task UpdateSoHeaderAsync(SoHeader soHeader)
        {
            await _dbConnection.UpdateAsync(soHeader);
        }

        public async Task<SoHeader?> GetSoHeaderByOrderNoAsync(string orderNo)
        {
            return await _dbConnection.Table<SoHeader>()
                .FirstOrDefaultAsync(h => h.Reference == orderNo);
        }

        public async Task DeleteSoAsync(string orderNo)
        {
            var soLines = await _dbConnection.Table<SoLine>().Where(p => p.DocNum == orderNo).ToListAsync();
            foreach (var line in soLines)
                await _dbConnection.DeleteAsync(line);

            var soHeader = await _dbConnection.Table<SoHeader>().FirstOrDefaultAsync(p => p.Reference == orderNo);
            if (soHeader != null)
                await _dbConnection.DeleteAsync(soHeader);
        }

        // Find SO line by barcode (for picking validation)
        public async Task<SoLine?> GetSoLineByBarcodeAsync(string orderNo, string barcode)
        {
            // First try to match by ItemBarcode (current behavior)
            var line = await _dbConnection.Table<SoLine>()
                .FirstOrDefaultAsync(l => l.DocNum == orderNo && l.ItemBarcode == barcode);

            if (line != null) return line;

            // If no match found, try to match by PackBarcode
            return await _dbConnection.Table<SoLine>()
                .FirstOrDefaultAsync(l => l.DocNum == orderNo &&
                                         !string.IsNullOrEmpty(l.PackBarcode) &&
                                         l.PackBarcode == barcode);
        }

        // Check if any user has started a phase at header level
        public async Task<bool> HasAnyUserStartedPhaseAsync(string orderNo, string phaseField)
        {
            var header = await GetSoHeaderByOrderNoAsync(orderNo);
            if (header == null) return false;

            switch (phaseField.ToLower())
            {
                case "picking":
                    return !string.IsNullOrEmpty(header.Picker);
                case "packing":
                    return !string.IsNullOrEmpty(header.Packer);
                case "checking":
                    return !string.IsNullOrEmpty(header.Checker);
                case "authorization":
                    return !string.IsNullOrEmpty(header.Authorizer);
                default:
                    return false;
            }
        }

        // Check if a specific user has started a phase at header level
        public async Task<bool> HasUserStartedPhaseAsync(string orderNo, string userName, string phaseField)
        {
            var header = await GetSoHeaderByOrderNoAsync(orderNo);
            if (header == null) return false;

            switch (phaseField.ToLower())
            {
                case "picking":
                    return header.Picker == userName;
                case "packing":
                    return header.Packer == userName;
                case "checking":
                    return header.Checker == userName;
                case "authorization":
                    return header.Authorizer == userName;
                default:
                    return false;
            }
        }

        // Set the user who started a phase at header level
        public async Task SetPhaseUserAsync(string orderNo, string userName, string phaseField)
        {
            var header = await GetSoHeaderByOrderNoAsync(orderNo);
            if (header == null) return;

            switch (phaseField.ToLower())
            {
                case "picking":
                    header.Picker = userName;
                    header.PickStarted = true;
                    break;
                case "packing":
                    header.Packer = userName;
                    break;
                case "checking":
                    header.Checker = userName;
                    break;
                case "authorization":
                    header.Authorizer = userName;
                    break;
            }

            await UpdateSoHeaderAsync(header);
        }

        // Check if authorization prerequisites are met (all previous phases must be complete)
        public async Task<bool> CanAuthorizeOrderAsync(string orderNo)
        {
            var lines = await GetSoLinesByOrderNoAsync(orderNo);
            if (!lines.Any()) return false;

            // All lines must have completed picking, packing, and checking phases
            return lines.All(l => l.Picked && l.Packed && l.Checked);
        }

        // Check if all lines in an SO have completed picking
        public async Task<bool> AreAllLinesPickedAsync(string orderNo)
        {
            var lines = await GetSoLinesByOrderNoAsync(orderNo);
            if (!lines.Any()) return false;
            return lines.All(l => l.Picked);
        }

        // Check if all lines in an SO have completed packing
        public async Task<bool> AreAllLinesPackedAsync(string orderNo)
        {
            var lines = await GetSoLinesByOrderNoAsync(orderNo);
            if (!lines.Any()) return false;
            return lines.All(l => l.Packed);
        }

        // Check if all lines in an SO have completed checking
        public async Task<bool> AreAllLinesCheckedAsync(string orderNo)
        {
            var lines = await GetSoLinesByOrderNoAsync(orderNo);
            if (!lines.Any()) return false;
            return lines.All(l => l.Checked);
        }

        // Check if all lines in an SO have completed authorization
        public async Task<bool> AreAllLinesAuthorizedAsync(string orderNo)
        {
            var lines = await GetSoLinesByOrderNoAsync(orderNo);
            if (!lines.Any()) return false;
            return lines.All(l => l.Authorized);
        }

    }
}
