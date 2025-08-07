using AmaScan.sqliteModels;
using Data.Model;
using SQLite;
using System.Collections.Concurrent;

namespace AmaScan.Classes
{
    public class PhaseStatus
    {
        public bool HasLines { get; set; }
        public bool AllPicked { get; set; }
        public bool AllPacked { get; set; }
        public bool AllChecked { get; set; }
        public bool AllAuthorized { get; set; }
    }

    public class DatabaseHelper
    {
        private readonly SQLiteAsyncConnection _dbConnection;
        
        // Cache for frequently accessed data
        private static readonly ConcurrentDictionary<string, StockItem> _stockItemCache = new();
        private static readonly ConcurrentDictionary<string, Warehouse> _warehouseCache = new();
        private static readonly ConcurrentDictionary<string, StockCountItem> _stockCountItemCache = new();
        private static readonly ConcurrentDictionary<string, PoHeader> _poHeaderCache = new();
        private static readonly ConcurrentDictionary<string, SoHeader> _soHeaderCache = new();
        private static readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(5);
        private static readonly ConcurrentDictionary<string, DateTime> _cacheTimestamps = new();

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
            
            // Invalidate cache for this PO
            if (!string.IsNullOrWhiteSpace(poHeader.OrderNo))
            {
                _poHeaderCache.TryRemove(poHeader.OrderNo, out _);
                _cacheTimestamps.TryRemove($"poheader_{poHeader.OrderNo}", out _);
            }
        }

        public async Task<PoHeader?> GetPoHeaderByOrderNoAsync(string orderNo)
        {
            if (string.IsNullOrWhiteSpace(orderNo))
                return null;

            // Check cache first
            if (_poHeaderCache.TryGetValue(orderNo, out var cachedHeader))
            {
                if (_cacheTimestamps.TryGetValue($"poheader_{orderNo}", out var timestamp) && 
                    DateTime.Now - timestamp < _cacheExpiration)
                {
                    return cachedHeader;
                }
                else
                {
                    _poHeaderCache.TryRemove(orderNo, out _);
                    _cacheTimestamps.TryRemove($"poheader_{orderNo}", out _);
                }
            }

            var poHeader = await _dbConnection.Table<PoHeader>()
                .FirstOrDefaultAsync(h => h.OrderNo == orderNo);

            if (poHeader != null)
            {
                // Cache the result
                _poHeaderCache.TryAdd(orderNo, poHeader);
                _cacheTimestamps.TryAdd($"poheader_{orderNo}", DateTime.Now);
            }

            return poHeader;
        }

        public async Task SaveStockItemsAsync(List<StockItem> items)
        {
            // Clear cache when updating stock items
            _stockItemCache.Clear();
            _cacheTimestamps.Clear();
            
            await _dbConnection.DeleteAllAsync<StockItem>();
            await _dbConnection.InsertAllAsync(items);
        }

        public async Task<StockItem?> ResolveStockItemByBarcodeAsync(string scannedBarcode)
        {
            if (string.IsNullOrWhiteSpace(scannedBarcode))
                return null;

            // Check cache first
            if (_stockItemCache.TryGetValue(scannedBarcode, out var cachedItem))
            {
                if (_cacheTimestamps.TryGetValue(scannedBarcode, out var timestamp) && 
                    DateTime.Now - timestamp < _cacheExpiration)
                {
                    return cachedItem;
                }
                else
                {
                    _stockItemCache.TryRemove(scannedBarcode, out _);
                    _cacheTimestamps.TryRemove(scannedBarcode, out _);
                }
            }

            // Optimize query by using case-insensitive comparison and combining queries
            var normalizedBarcode = scannedBarcode.ToLower();
            var stockItem = await _dbConnection.Table<StockItem>()
                .Where(item => 
                    (item.bar_code != null && item.bar_code.ToLower() == normalizedBarcode) ||
                    (item.barcode_lmmp != null && item.barcode_lmmp.ToLower() == normalizedBarcode))
                .FirstOrDefaultAsync();

            if (stockItem != null)
            {
                // Cache the result
                _stockItemCache.TryAdd(scannedBarcode, stockItem);
                _cacheTimestamps.TryAdd(scannedBarcode, DateTime.Now);
                return stockItem;
            }

            // If no direct match, check alternate barcodes with optimized query
            var result = await _dbConnection.Table<StockItem>()
                .Where(item => 
                    item.alternate_bar_codes != null && 
                    item.alternate_bar_codes != "" && 
                    item.alternate_bar_codes.Contains(scannedBarcode))
                .FirstOrDefaultAsync();

            // Additional check for exact match in comma-separated list
            if (result != null)
            {
                var alternateBarcodes = result.alternate_bar_codes
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                
                if (!alternateBarcodes.Any(b => b.ToLower() == normalizedBarcode))
                {
                    result = null; // No exact match found
                }
            }

            if (result != null)
            {
                // Cache the result
                _stockItemCache.TryAdd(scannedBarcode, result);
                _cacheTimestamps.TryAdd(scannedBarcode, DateTime.Now);
            }

            return result;
        }

        public async Task DeletePoAsync(string poNumber)
        {
            // Delete PO lines using a query instead of loading all into memory
            await _dbConnection.Table<PoLine>().Where(p => p.OrderNo == poNumber).DeleteAsync();

            var poHeader = await _dbConnection.Table<PoHeader>().FirstOrDefaultAsync(p => p.OrderNo == poNumber);
            if (poHeader != null)
                await _dbConnection.DeleteAsync(poHeader);
        }

        public async Task DeleteAllExceptPoAsync(string poNumber)
        {
            // Delete PO lines using a query instead of loading all into memoryI j
            await _dbConnection.Table<PoLine>().Where(p => p.OrderNo != poNumber).DeleteAsync();

            var poHeader = await _dbConnection.Table<PoHeader>().FirstOrDefaultAsync(p => p.OrderNo != poNumber);
            if (poHeader != null) await _dbConnection.DeleteAsync(poHeader);
        }

        /// <summary>
        /// Merges fresh PO data from API with existing local workflow data
        /// Preserves workflow-specific fields while updating source data
        /// </summary>
        public async Task MergePoDataAsync(PurchaseOrderResponse freshData)
        {
            if (freshData?.Lines == null || !freshData.Lines.Any())
                throw new ArgumentException("Invalid purchase order data for merge.");

            var existingHeader = await GetPoHeaderByOrderNoAsync(freshData.OrderNo);
            var existingLines = await GetPoLinesByOrderNoAsync(freshData.OrderNo);

            // Update header with fresh data
            if (existingHeader != null)
            {
                existingHeader.DueDate = freshData.DueDate;
                existingHeader.Status = freshData.Status;
                existingHeader.SupplierName = freshData.SupplierName;
                existingHeader.JsonData = System.Text.Json.JsonSerializer.Serialize(freshData);
                // Preserve workflow fields: DNnumber, SuppInvNumber, iscompleted
                await UpdatePoHeaderAsync(existingHeader);
            }
            else
            {
                // Create new header if doesn't exist
                var newHeader = new PoHeader
                {
                    OrderNo = freshData.OrderNo,
                    DueDate = freshData.DueDate,
                    Status = freshData.Status,
                    JsonData = System.Text.Json.JsonSerializer.Serialize(freshData),
                    iscompleted = false
                };
                await InsertAsync(newHeader);
            }

            // Create lookup dictionaries for efficient matching using composite key
            var existingLinesDict = existingLines.ToDictionary(l => $"{l.ItemCode}_{l.ItemBarcode}", l => l);
            var freshLinesDict = freshData.Lines.ToDictionary(l => $"{l.ItemCode}_{l.ItemBarcode}", l => l);

            var linesToUpdate = new List<PoLine>();
            var linesToInsert = new List<PoLine>();
            var linesToDelete = new List<PoLine>();
            bool hasQuantityDecreases = false;

            // Process fresh lines
            foreach (var freshLine in freshData.Lines)
            {
                var key = $"{freshLine.ItemCode}_{freshLine.ItemBarcode}";
                if (existingLinesDict.TryGetValue(key, out var existingLine))
                {
                    // Check if quantity decreased
                    if (freshLine.OrderedQty < existingLine.OrderedQty)
                    {
                        hasQuantityDecreases = true;
                    }

                    // Update existing line with fresh data while preserving workflow data
                    UpdatePoLineFromFreshData(existingLine, freshLine);
                    linesToUpdate.Add(existingLine);
                }
                else
                {
                    // Create new line
                    var newPoLine = CreateNewPoLine(freshData.OrderNo, freshLine);
                    linesToInsert.Add(newPoLine);
                }
            }

            // Find lines to delete
            linesToDelete = existingLines.Where(l => !freshLinesDict.ContainsKey($"{l.ItemCode}_{l.ItemBarcode}")).ToList();

            // Batch database operations
            if (linesToDelete.Any())
            {
                foreach (var line in linesToDelete)
                    await _dbConnection.DeleteAsync(line);
            }

            if (linesToInsert.Any())
                await _dbConnection.InsertAllAsync(linesToInsert);

            // Batch update all modified lines
            if (linesToUpdate.Any())
                await _dbConnection.UpdateAllAsync(linesToUpdate);

            // Show alert for quantity decreases
            if (hasQuantityDecreases)
            {
                await Application.Current.MainPage.DisplayAlert("Quantities Reduced",
                    "Some item quantities have been reduced. All receiving progress for these items has been reset to zero.",
                    "OK");
            }
            
            // Clear PO cache when data is merged
            if (!string.IsNullOrWhiteSpace(freshData.OrderNo))
            {
                _poHeaderCache.TryRemove(freshData.OrderNo, out _);
                _cacheTimestamps.TryRemove($"poheader_{freshData.OrderNo}", out _);
            }
        }

        private void UpdatePoLineFromFreshData(PoLine existingLine, PurchaseOrderLine freshLine)
        {
            // Check if quantity decreased - if so, zero out workflow progress
            if (freshLine.OrderedQty < existingLine.OrderedQty)
            {
                // Zero out all workflow quantities and reset flags
                existingLine.ScanAcceptQty = 0;
                existingLine.ScanRejectQty = 0;
                existingLine.ReceivedQty = 0;
                existingLine.ReceivedString = null;
                existingLine.GRNum = null;
            }
            else
            {
                // Preserve workflow-specific data only if quantity didn't decrease
                var preservedScanAcceptQty = existingLine.ScanAcceptQty;
                var preservedScanRejectQty = existingLine.ScanRejectQty;
                var preservedReceivedQty = existingLine.ReceivedQty;
                var preservedGRNum = existingLine.GRNum;
                var preservedReceivedString = existingLine.ReceivedString;

                // Restore preserved workflow data
                existingLine.ScanAcceptQty = preservedScanAcceptQty;
                existingLine.ScanRejectQty = preservedScanRejectQty;
                existingLine.ReceivedQty = preservedReceivedQty;
                existingLine.GRNum = preservedGRNum;
                existingLine.ReceivedString = preservedReceivedString;
            }

            // Update with fresh data
            existingLine.ItemCode = freshLine.ItemCode;
            existingLine.ItemDesc = freshLine.ItemDesc;
            existingLine.ItemBarcode = freshLine.ItemBarcode;
            existingLine.PackBarcode = freshLine.PackBarcode;
            existingLine.PackSize = freshLine.PackSize;
            existingLine.NoOfPacks = freshLine.no_of_packs;
            existingLine.OrderedQty = freshLine.OrderedQty;
            existingLine.BinLocation = freshLine.BinLocation;
            existingLine.WhID = freshLine.WhID;
        }

        private PoLine CreateNewPoLine(string orderNo, PurchaseOrderLine freshLine)
        {
            return new PoLine
            {
                OrderNo = freshLine.DocNum,
                LineNo = freshLine.LineNo,
                ItemCode = freshLine.ItemCode,
                ItemDesc = freshLine.ItemDesc,
                ItemBarcode = freshLine.ItemBarcode,
                PackBarcode = freshLine.PackBarcode,
                PackSize = freshLine.PackSize,
                NoOfPacks = freshLine.no_of_packs,
                OrderedQty = freshLine.OrderedQty,
                ReceivedQty = 0, // New line starts with 0
                ScanAcceptQty = 0, // New line starts with 0
                ScanRejectQty = 0, // New line starts with 0
                BinLocation = freshLine.BinLocation,
                WhID = freshLine.WhID,
                GRNum = null
            };
        }

        // Save (replace all) warehouses from API to local DB
        public async Task SaveWarehousesAsync(List<Warehouse> warehouses)
        {
            // Clear cache when updating warehouses
            _warehouseCache.Clear();
            _cacheTimestamps.Clear();
            
            await _dbConnection.DeleteAllAsync<Warehouse>();
            await _dbConnection.InsertAllAsync(warehouses);
        }

        // Get all warehouses
        public Task<List<Warehouse>> GetWarehousesAsync()
        {
            return _dbConnection.Table<Warehouse>().ToListAsync();
        }

        // Get a warehouse by code with caching
        public async Task<Warehouse?> GetWarehouseByCodeAsync(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return null;

            // Check cache first
            if (_warehouseCache.TryGetValue(code, out var cachedWarehouse))
            {
                if (_cacheTimestamps.TryGetValue($"warehouse_{code}", out var timestamp) && 
                    DateTime.Now - timestamp < _cacheExpiration)
                {
                    return cachedWarehouse;
                }
                else
                {
                    _warehouseCache.TryRemove(code, out _);
                    _cacheTimestamps.TryRemove($"warehouse_{code}", out _);
                }
            }

            var warehouse = await _dbConnection.Table<Warehouse>()
                                .FirstOrDefaultAsync(w => w.Code == code);

            if (warehouse != null)
            {
                // Cache the result
                _warehouseCache.TryAdd(code, warehouse);
                _cacheTimestamps.TryAdd($"warehouse_{code}", DateTime.Now);
            }

            return warehouse;
        }

        // Get description by code (utility method) with caching
        public async Task<string?> GetWarehouseDescriptionAsync(string code)
        {
            var warehouse = await GetWarehouseByCodeAsync(code);
            return warehouse?.Description;
        }

        // Optional: Delete all warehouses
        public Task<int> ClearWarehousesAsync()
        {
            // Clear cache
            _warehouseCache.Clear();
            _cacheTimestamps.Clear();
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
            orderNo = FixOrderNo(orderNo);
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
            
            // Invalidate cache for this SO
            if (!string.IsNullOrWhiteSpace(soHeader.Reference))
            {
                _soHeaderCache.TryRemove(soHeader.Reference, out _);
                _cacheTimestamps.TryRemove($"soheader_{soHeader.Reference}", out _);
            }
        }

        public async Task<SoHeader?> GetSoHeaderByOrderNoAsync(string orderNo)
        {
            if (string.IsNullOrWhiteSpace(orderNo))
                return null;

            orderNo = FixOrderNo(orderNo);

            // Check cache first
            if (_soHeaderCache.TryGetValue(orderNo, out var cachedHeader))
            {
                if (_cacheTimestamps.TryGetValue($"soheader_{orderNo}", out var timestamp) && 
                    DateTime.Now - timestamp < _cacheExpiration)
                {
                    return cachedHeader;
                }
                else
                {
                    _soHeaderCache.TryRemove(orderNo, out _);
                    _cacheTimestamps.TryRemove($"soheader_{orderNo}", out _);
                }
            }

            var soHeader = await _dbConnection.Table<SoHeader>()
                .FirstOrDefaultAsync(h => h.Reference == orderNo);

            if (soHeader != null)
            {
                // Cache the result
                _soHeaderCache.TryAdd(orderNo, soHeader);
                _cacheTimestamps.TryAdd($"soheader_{orderNo}", DateTime.Now);
            }

            return soHeader;
        }

        public async Task DeleteSoAsync(string orderNo)
        {
            orderNo = FixOrderNo(orderNo);
            
            // Delete SO lines using a query instead of loading all into memory
            await _dbConnection.Table<SoLine>().Where(p => p.DocNum == orderNo).DeleteAsync();

            var soHeader = await _dbConnection.Table<SoHeader>().FirstOrDefaultAsync(p => p.Reference == orderNo);
            if (soHeader != null)
                await _dbConnection.DeleteAsync(soHeader);
        }

        public async Task DeleteAllExceptSoAsync(string orderNo)
        {
            orderNo = FixOrderNo(orderNo);

            // Delete SO lines using a query instead of loading all into memory
            await _dbConnection.Table<SoLine>().Where(p => p.DocNum != orderNo).DeleteAsync();

            var soHeader = await _dbConnection.Table<SoHeader>().FirstOrDefaultAsync(p => p.Reference != orderNo);
            if (soHeader != null) await _dbConnection.DeleteAsync(soHeader);
        }
        /// <summary>
        /// Merges fresh SO data from API with existing local workflow data
        /// Preserves workflow-specific fields while updating source data
        /// </summary>
        public async Task MergeSoDataAsync(SalesOrderResponse freshData)
        {
            if (freshData?.Lines == null || !freshData.Lines.Any())
                throw new ArgumentException("Invalid sales order data for merge.");

            var existingHeader = await GetSoHeaderByOrderNoAsync(freshData.Reference);
            var existingLines = await GetSoLinesByOrderNoAsync(freshData.Reference);

            // Update or create header
            if (existingHeader != null)
            {
                existingHeader.CustomerOrderNo = freshData.CustomerOrderNo;
                existingHeader.CustomerName = freshData.CustomerName;
                existingHeader.AreaDescription = freshData.AreaDescription;
                existingHeader.DueDate = freshData.DueDate;
                existingHeader.OrderStatus = freshData.OrderStatus;
                existingHeader.JsonData = System.Text.Json.JsonSerializer.Serialize(freshData);
            }
            else
            {
                existingHeader = new SoHeader
                {
                    Reference = freshData.Reference,
                    CustomerOrderNo = freshData.CustomerOrderNo,
                    CustomerName = freshData.CustomerName,
                    AreaDescription = freshData.AreaDescription,
                    DueDate = freshData.DueDate,
                    OrderStatus = freshData.OrderStatus,
                    JsonData = System.Text.Json.JsonSerializer.Serialize(freshData)
                };
                await InsertAsync(existingHeader);
            }

            // Create lookup dictionaries
            var existingLinesDict = existingLines.ToDictionary(l => $"{l.ItemCode}_{l.ItemBarcode}", l => l);
            var freshLinesDict = freshData.Lines.ToDictionary(l => $"{l.ItemCode}_{l.ItemBarcode}", l => l);

            var linesToUpdate = new List<SoLine>();
            var linesToInsert = new List<SoLine>();
            var linesToDelete = new List<SoLine>();
            bool hasQuantityDecreases = false;

            // Process fresh lines
            foreach (var freshLine in freshData.Lines)
            {
                var key = $"{freshLine.ItemCode}_{freshLine.ItemBarcode}";
                if (existingLinesDict.TryGetValue(key, out var existingLine))
                {
                    // Check if quantity decreased
                    if (freshLine.OrderedQty < existingLine.OrderedQty)
                    {
                        hasQuantityDecreases = true;
                    }

                    // Update existing line
                    UpdateSoLineFromFreshData(existingLine, freshLine);
                    linesToUpdate.Add(existingLine);
                }
                else
                {
                    // Create new line
                    var newLine = CreateNewSoLine(freshData.Reference, freshLine);
                    linesToInsert.Add(newLine);
                }
            }

            // Find lines to delete
            linesToDelete = existingLines.Where(l => !freshLinesDict.ContainsKey($"{l.ItemCode}_{l.ItemBarcode}")).ToList();

            // Batch database operations
            if (linesToDelete.Any())
            {
                foreach (var line in linesToDelete)
                    await _dbConnection.DeleteAsync(line);
            }

            if (linesToInsert.Any())
                await _dbConnection.InsertAllAsync(linesToInsert);

            // Update workflow status for all lines
            var allLines = linesToUpdate.Concat(linesToInsert).ToList();
            var workflowChanged = UpdateWorkflowStatus(allLines, existingHeader);

            // Batch update all modified lines
            if (linesToUpdate.Any())
                await _dbConnection.UpdateAllAsync(linesToUpdate);

            // Update header if workflow status changed
            if (workflowChanged)
            {
                await UpdateSoHeaderAsync(existingHeader);

                // Show notification if new lines were added
                if (linesToInsert.Any())
                {
                    await Application.Current.MainPage.DisplayAlert(
                        "SO Updated",
                        "New lines were added to this Sales Order.",
                        "OK"
                    );
                }
            }

            // Show alert for quantity decreases
            if (hasQuantityDecreases)
            {
                await Application.Current.MainPage.DisplayAlert("Quantities Reduced",
                    "Some item quantities have been reduced. All workflow progress for these items has been reset to zero.",
                    "OK");
            }
            
            // Clear SO cache when data is merged
            if (!string.IsNullOrWhiteSpace(freshData.Reference))
            {
                _soHeaderCache.TryRemove(freshData.Reference, out _);
                _cacheTimestamps.TryRemove($"soheader_{freshData.Reference}", out _);
            }
        }

        private void UpdateSoLineFromFreshData(SoLine existingLine, SalesOrderLine freshLine)
        {

            // Check if quantity decreased - if so, zero out workflow progress
            if (freshLine.OrderedQty < existingLine.OrderedQty)
            {
                // Zero out all workflow quantities and reset flags
                existingLine.PickedQty = 0;
                existingLine.PackedQty = 0;
                existingLine.CheckedQty = 0;
                existingLine.AuthorizedQty = 0;
                existingLine.Picked = false;
                existingLine.Packed = false;
                existingLine.Checked = false;
                existingLine.Authorized = false;
                existingLine.PickStarted = false;
                existingLine.PackStarted = false;
                existingLine.CheckStarted = false;
                existingLine.AuthStarted = false;
                existingLine.PickedBy = null;
                existingLine.PackedBy = null;
                existingLine.CheckedBy = null;
                existingLine.AuthorizedBy = null;
                existingLine.PickStartDateTime = null;
                existingLine.PackStartDateTime = null;
                existingLine.CheckStartDateTime = null;
                existingLine.AuthStartDateTime = null;
                existingLine.PickCompleteDateTime = null;
                existingLine.PackCompleteDateTime = null;
                existingLine.CheckCompleteDateTime = null;
                existingLine.AuthCompleteDateTime = null;
            }

            // Update source data
            existingLine.ItemDesc = freshLine.ItemDesc;
            existingLine.PackBarcode = freshLine.PackBarcode;
            existingLine.PackSize = freshLine.PackSize;
            existingLine.NoOfPacks = freshLine.NoOfPacks;
            existingLine.OrderedQty = freshLine.OrderedQty;
            existingLine.Bin = freshLine.Bin;
        }

        private SoLine CreateNewSoLine(string orderNo, SalesOrderLine freshLine)
        {
            return new SoLine
            {
                DocNum = orderNo,
                CustomerAccount = freshLine.CustomerAccount,
                CustomerName = freshLine.CustomerName,
                ItemCode = freshLine.ItemCode,
                ItemDesc = freshLine.ItemDesc,
                ItemBarcode = freshLine.ItemBarcode,
                PackSize = freshLine.PackSize,
                PackBarcode = freshLine.PackBarcode,
                NoOfPacks = freshLine.NoOfPacks,
                OrderedQty = freshLine.OrderedQty,
                PickedQty = 0,
                PackedQty = 0,
                CheckedQty = 0,
                AuthorizedQty = 0,
                Bin = freshLine.Bin,
                Picked = false,
                Packed = false,
                Checked = false,
                Authorized = false
            };
        }

        private bool UpdateWorkflowStatus(List<SoLine> lines, SoHeader header)
        {
            bool headerChanged = false;
            // Check workflow status
            var hasIncompletePicking = lines.Any(l => !l.Picked);
            var hasIncompletePacking = lines.Any(l => !l.Packed);
            var hasIncompleteChecking = lines.Any(l => !l.Checked);

            foreach (var line in lines)
            {
                // Update completion status based on quantities
                line.Picked = line.PickedQty >= line.OrderedQty;
                line.Packed = line.PackedQty >= line.OrderedQty;
                line.Checked = line.CheckedQty >= line.OrderedQty;
                line.Authorized = line.AuthorizedQty >= line.OrderedQty;

                // Track incomplete phases
                if (!line.Picked) hasIncompletePicking = true;
                if (!line.Packed) hasIncompletePacking = true;
                if (!line.Checked) hasIncompleteChecking = true;

                // Clear downstream *Started flags when phases are incomplete
                if (!line.Picked)
                {
                    line.PackStarted = false;
                    line.CheckStarted = false;
                    line.AuthStarted = false;
                }
                else if (!line.Packed)
                {
                    line.CheckStarted = false;
                    line.AuthStarted = false;
                }
                else if (!line.Checked)
                {
                    line.AuthStarted = false;
                }
            }

            // Update header flags based on line status
            var allPicked = lines.All(l => l.Picked);
            var allPacked = lines.All(l => l.Packed);
            var allChecked = lines.All(l => l.Checked);
            var allAuthorized = lines.All(l => l.Authorized);

            if (header.Picked != allPicked) { header.Picked = allPicked; headerChanged = true; }
            if (header.Packed != allPacked) { header.Packed = allPacked; headerChanged = true; }
            if (header.Checked != allChecked) { header.Checked = allChecked; headerChanged = true; }
            if (header.Authed != allAuthorized) { header.Authed = allAuthorized; headerChanged = true; }

            return headerChanged;
        }

        // Find SO line by barcode (for picking validation)
        public async Task<SoLine?> GetSoLineByBarcodeAsync(string orderNo, string barcode)
        {
            orderNo = FixOrderNo(orderNo);

            // Single query to check both ItemBarcode and PackBarcode
            var line = await _dbConnection.Table<SoLine>()
                .FirstOrDefaultAsync(l => l.DocNum == orderNo &&
                                         (l.ItemBarcode == barcode || l.PackBarcode == barcode));

            return line;
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

        // Get phase completion status for all phases in one query
        public async Task<PhaseStatus> GetPhaseStatusAsync(string orderNo)
        {
            var lines = await GetSoLinesByOrderNoAsync(orderNo);
            if (!lines.Any())
                return new PhaseStatus { HasLines = false };

            return new PhaseStatus
            {
                HasLines = true,
                AllPicked = lines.All(l => l.Picked),
                AllPacked = lines.All(l => l.Packed),
                AllChecked = lines.All(l => l.Checked),
                AllAuthorized = lines.All(l => l.Authorized)
            };
        }

        public async Task<bool> AreAllLinesPickedAsync(string orderNo)
        {
            var status = await GetPhaseStatusAsync(orderNo);
            return status.HasLines && status.AllPicked;
        }

        public async Task<bool> AreAllLinesPackedAsync(string orderNo)
        {
            var status = await GetPhaseStatusAsync(orderNo);
            return status.HasLines && status.AllPacked;
        }

        public async Task<bool> AreAllLinesCheckedAsync(string orderNo)
        {
            var status = await GetPhaseStatusAsync(orderNo);
            return status.HasLines && status.AllChecked;
        }

        public async Task<bool> AreAllLinesAuthorizedAsync(string orderNo)
        {
            var status = await GetPhaseStatusAsync(orderNo);
            return status.HasLines && status.AllAuthorized;
        }

        public static string FixOrderNo(string orderNo)
        {
            if (!orderNo.StartsWith("IO"))
            {
                orderNo = $"IO{orderNo}";
            }
            return orderNo;
        }



        public async Task SaveReturnLineAsync(ReturnLine returnLine, string userName = null)
        {
            ArgumentNullException.ThrowIfNull(returnLine);

            // Set the warehouse code from preferences
            returnLine.WarehouseCode = Preferences.Get("ReturnsWarehouseCode", "");

            // Set the processed by user
            returnLine.ProcessedBy = userName ?? "Unknown User";

            // Set processed timestamp
            returnLine.ProcessedDateTime = DateTime.Now;
            returnLine.Processed = true;

            // Insert the return line into the database
            await _dbConnection.InsertAsync(returnLine);
        }

        // Stock Count Operations
        public async Task SaveStockCountItemsAsync(List<StockCountItem> items)
        {
            // Get existing items to preserve progress
            var existingItems = await _dbConnection.Table<StockCountItem>().ToListAsync();
            
            // Create lookup for existing items by StockCode
            var existingLookup = existingItems.ToDictionary(x => x.StockCode, x => x);
            
            // Process each new item
            foreach (var newItem in items)
            {
                if (existingLookup.TryGetValue(newItem.StockCode, out var existingItem))
                {
                    // Preserve counting progress from existing item
                    newItem.Count1Qty = existingItem.Count1Qty;
                    newItem.Count2Qty = existingItem.Count2Qty;
                    newItem.ConfirmCountQty = existingItem.ConfirmCountQty;
                    newItem.CountBy = existingItem.CountBy;
                    newItem.ConfirmBy = existingItem.ConfirmBy;
                    newItem.CountComplete = existingItem.CountComplete;
                    newItem.CountString = existingItem.CountString;
                    newItem.Phase1Complete = existingItem.Phase1Complete;
                    newItem.Phase2Complete = existingItem.Phase2Complete;
                }
            }
            
            // Replace all items (preserving progress)
            await _dbConnection.DeleteAllAsync<StockCountItem>();
            await _dbConnection.InsertAllAsync(items);
            
            // Clear stock count cache when data is updated
            _stockCountItemCache.Clear();
            foreach (var key in _cacheTimestamps.Keys.Where(k => k.StartsWith("stockcount_")))
            {
                _cacheTimestamps.TryRemove(key, out _);
            }
        }

        public async Task<List<StockCountItem>> GetStockCountItemsAsync()
        {
            return await _dbConnection.Table<StockCountItem>().ToListAsync();
        }



        public async Task<StockCountItem?> GetStockCountItemByCodeAsync(string stockCode)
        {
            if (string.IsNullOrWhiteSpace(stockCode))
                return null;

            // Check cache first
            if (_stockCountItemCache.TryGetValue(stockCode, out var cachedItem))
            {
                if (_cacheTimestamps.TryGetValue($"stockcount_{stockCode}", out var timestamp) && 
                    DateTime.Now - timestamp < _cacheExpiration)
                {
                    return cachedItem;
                }
                else
                {
                    _stockCountItemCache.TryRemove(stockCode, out _);
                    _cacheTimestamps.TryRemove($"stockcount_{stockCode}", out _);
                }
            }

            var stockCountItem = await _dbConnection.Table<StockCountItem>()
                .FirstOrDefaultAsync(item => item.StockCode == stockCode);

            if (stockCountItem != null)
            {
                // Cache the result
                _stockCountItemCache.TryAdd(stockCode, stockCountItem);
                _cacheTimestamps.TryAdd($"stockcount_{stockCode}", DateTime.Now);
            }

            return stockCountItem;
        }

        public async Task<StockCountItem?> GetStockCountItemByCodeAndBatchAsync(string stockCode, string batchNo)
        {
            return await _dbConnection.Table<StockCountItem>()
                .FirstOrDefaultAsync(item => item.StockCode == stockCode && item.BatchNo == batchNo);
        }

        public async Task UpdateStockCountItemAsync(StockCountItem item)
        {
            // Use a more efficient update operation
            await _dbConnection.UpdateAsync(item);
            
            // Invalidate cache for this item to ensure data consistency
            if (!string.IsNullOrWhiteSpace(item.StockCode))
            {
                _stockCountItemCache.TryRemove(item.StockCode, out _);
                _cacheTimestamps.TryRemove($"stockcount_{item.StockCode}", out _);
            }
        }

        public async Task<List<StockCountItem>> GetIncompleteStockCountsAsync()
        {
            return await _dbConnection.Table<StockCountItem>()
                .Where(item => !item.CountComplete)
                .ToListAsync();
        }


        public async Task<StockCountItem?> ResolveStockCountItemByBarcodeAsync(string scannedBarcode)
        {
            if (string.IsNullOrWhiteSpace(scannedBarcode))
                return null;

            // Use database queries instead of loading all items into memory
            var stockCountItem = await _dbConnection.Table<StockCountItem>()
                .Where(item => 
                    (!string.IsNullOrWhiteSpace(item.BarCode) && item.BarCode.Equals(scannedBarcode, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(item.BarcodeLmmp) && item.BarcodeLmmp.Equals(scannedBarcode, StringComparison.OrdinalIgnoreCase)))
                .FirstOrDefaultAsync();

            if (stockCountItem != null)
                return stockCountItem;

            // If no direct match, check additional barcodes from StockItem table
            var stockItems = await _dbConnection.Table<StockItem>()
                .Where(item => !string.IsNullOrWhiteSpace(item.alternate_bar_codes))
                .ToListAsync();

            foreach (var stockItem in stockItems)
            {
                if (!string.IsNullOrWhiteSpace(stockItem.alternate_bar_codes))
                {
                    var additionalBarcodes = stockItem.alternate_bar_codes
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    
                    if (additionalBarcodes.Any(b => b.Equals(scannedBarcode, StringComparison.OrdinalIgnoreCase)))
                    {
                        // Find corresponding StockCountItem
                        return await _dbConnection.Table<StockCountItem>()
                            .FirstOrDefaultAsync(item => item.StockCode == stockItem.stock_code);
                    }
                }
            }

            return null;
        }
    }
}
