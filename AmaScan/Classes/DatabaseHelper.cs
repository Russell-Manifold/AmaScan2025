using AmaScan.sqliteModels;
using Data.Model;
using SQLite;
using System.Collections.Concurrent;
using System.Diagnostics;

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
        // expose it
        public SQLiteAsyncConnection Connection => _dbConnection;
  
        // Cache for frequently accessed data
        private static readonly ConcurrentDictionary<string, StockItem> _stockItemCache = new();
        private static readonly ConcurrentDictionary<string, Warehouse> _warehouseCache = new();
        private static readonly ConcurrentDictionary<string, StockCountItem> _stockCountItemCache = new();
        private static readonly ConcurrentDictionary<string, PoHeader> _poHeaderCache = new();
        private static readonly ConcurrentDictionary<string, SoHeader> _soHeaderCache = new();
        private static readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(10);
        private static readonly ConcurrentDictionary<string, DateTime> _cacheTimestamps = new();
        private static readonly object _dbLock = new object();

        public DatabaseHelper(SQLiteAsyncConnection dbConnection)
        {
            _dbConnection = dbConnection ?? throw new ArgumentNullException(nameof(dbConnection));
        }

        public Task<int> InsertAsync<T>(T item)
        {
            return _dbConnection.RunInTransactionAsync(conn =>
            {
                conn.Insert(item);
            }).ContinueWith(_ => 1);
        }

        public Task<int> UpdateAsync<T>(T item)
        {
            return _dbConnection.RunInTransactionAsync(conn =>
            {
                conn.Update(item);
            }).ContinueWith(_ => 1);
        }

        public async Task InsertAllAsync<T>(IEnumerable<T> items) where T : new()
        {
            if (items == null || !items.Any())
                return;

            await _dbConnection.RunInTransactionAsync(conn =>
            {
                conn.InsertAll(items);
            }).ConfigureAwait(false);
        }
        public Task<int> DeleteAsync<T>(T item)
        {
            return _dbConnection.RunInTransactionAsync(conn =>
            {
                conn.Delete(item);
            }).ContinueWith(_ => 1);
        }
        public async Task UpdateAllAsync<T>(IEnumerable<T> items) where T : new()
        {
            if (items == null || !items.Any())
                return;

            await _dbConnection.RunInTransactionAsync(conn =>
            {
                conn.UpdateAll(items);
            }).ConfigureAwait(false);
        }


        public async Task<T> GetItemAsync<T>(int id) where T : IIdentifiable, new()
        {
            return await _dbConnection.Table<T>()
                                  .Where(i => i.Id == id)
                                  .FirstOrDefaultAsync()
                                  .ConfigureAwait(false);
        }
        public Task<List<StockItem>> GetAllStockItemsAsync()
        {
            return _dbConnection.Table<StockItem>().ToListAsync();
        }

        public Task<List<T>> GetItemsAsync<T>() where T : new()
        {
            return _dbConnection.Table<T>().ToListAsync();
        }

        public async Task<List<PoLine>> GetPoLinesByOrderNoAsync(string orderNo)
        {
            return await _dbConnection.Table<PoLine>()
                .Where(line => line.OrderNo == orderNo)
                .OrderBy(line => line.LineNo)
                .ToListAsync()
                .ConfigureAwait(false);
        }

        public async Task UpdatePoLineAsync(PoLine poLine)
        {
            await _dbConnection.RunInTransactionAsync(conn =>
            {
                conn.Update(poLine);
            }).ConfigureAwait(false);
        }

        public async Task UpdatePoHeaderAsync(PoHeader poHeader, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await _dbConnection.RunInTransactionAsync(conn =>
            {
                // SQLite doesn't support async inside transaction, so keep it sync
                conn.Update(poHeader);
            }).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

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

            if (_poHeaderCache.TryGetValue(orderNo, out var cachedHeader))
            {
                if (_cacheTimestamps.TryGetValue($"poheader_{orderNo}", out var timestamp) &&
                    DateTime.Now - timestamp < _cacheExpiration)
                {
                    return cachedHeader;
                }

                _poHeaderCache.TryRemove(orderNo, out _);
                _cacheTimestamps.TryRemove($"poheader_{orderNo}", out _);
            }

            var poHeader = await _dbConnection.Table<PoHeader>()
                .FirstOrDefaultAsync(h => h.OrderNo == orderNo)
                .ConfigureAwait(false);

            if (poHeader != null)
            {
                _poHeaderCache.TryAdd(orderNo, poHeader);
                _cacheTimestamps.TryAdd($"poheader_{orderNo}", DateTime.Now);
            }

            return poHeader;
        }

        public async Task SaveStockItemsAsync(List<StockItem> items)
        {
            _stockItemCache.Clear();
            _cacheTimestamps.Clear();

            await _dbConnection.RunInTransactionAsync(conn =>
            {
                conn.DeleteAll<StockItem>();
                conn.InsertAll(items);
            }).ConfigureAwait(false);
        }

        public async Task<StockItem?> ResolveStockItemByBarcodeAsync(string scannedBarcode)
        {
            if (string.IsNullOrWhiteSpace(scannedBarcode))
                return null;

            // 1. Memory cache (ConcurrentDictionary is already thread-safe)
            if (_stockItemCache.TryGetValue(scannedBarcode, out var cachedItem) &&
                _cacheTimestamps.TryGetValue(scannedBarcode, out var ts) &&
                DateTime.UtcNow - ts < _cacheExpiration)
            {
                return cachedItem;
            }

            _stockItemCache.TryRemove(scannedBarcode, out _);
            _cacheTimestamps.TryRemove(scannedBarcode, out _);

            // 2. Single query: exact barcode OR in alternate list (case-insensitive)
            var param = scannedBarcode.ToLowerInvariant();   // allocate once
            var stockItem = await _dbConnection.Table<StockItem>()
                .Where(si =>
                    (si.bar_code != null && si.bar_code.ToLower() == param) ||
                    (si.barcode_lmmp != null && si.barcode_lmmp.ToLower() == param) ||
                    (si.alternate_bar_codes != null &&
                     si.alternate_bar_codes.ToLower().Contains(param)))   // LIKE '%code%'
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);

            // 3. If we got a hit based on alternate list, double-check it is a *whole* token
            if (stockItem != null &&
                stockItem.bar_code?.ToLower() != param &&
                stockItem.barcode_lmmp?.ToLower() != param)
            {
                var tokens = stockItem.alternate_bar_codes?
                                       .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                       .Select(t => t.ToLowerInvariant());
                if (tokens == null || !tokens.Contains(param))
                    stockItem = null;
            }

            // 4. Cache the final result (even null to avoid re-querying)
            if (stockItem != null)
            {
                _stockItemCache.TryAdd(scannedBarcode, stockItem);
                _cacheTimestamps.TryAdd(scannedBarcode, DateTime.UtcNow);
            }

            return stockItem;
        }

        public async Task DeletePoAsync(string poNumber)
        {
            await _dbConnection.RunInTransactionAsync(conn =>
            {
                conn.Execute("DELETE FROM PoLine WHERE OrderNo = ?", poNumber);
                conn.Execute("DELETE FROM PoHeader WHERE OrderNo = ?", poNumber);
            }).ConfigureAwait(false);
        }

        public async Task DeleteAllExceptPoAsync(string poNumber, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await _dbConnection.RunInTransactionAsync(conn =>
            {
                conn.Execute("DELETE FROM PoLine WHERE OrderNo != ?", poNumber);
                conn.Execute("DELETE FROM PoHeader WHERE OrderNo != ?", poNumber);
            }).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
        }

        public async Task MergePoDataAsync(PurchaseOrderResponse freshData)
        {
            if (freshData?.Lines == null || !freshData.Lines.Any())
                throw new ArgumentException("Invalid purchase order data for merge.");

            await _dbConnection.RunInTransactionAsync(conn =>
            {
                var existingHeader = conn.Table<PoHeader>()
                    .FirstOrDefault(h => h.OrderNo == freshData.OrderNo);

                var existingLines = conn.Table<PoLine>()
                    .Where(l => l.OrderNo == freshData.OrderNo)
                    .ToList();

                var existingLinesDict = existingLines.ToDictionary(l => $"{l.ItemCode}_{l.ItemBarcode}", l => l);
                var freshLinesDict = freshData.Lines.ToDictionary(l => $"{l.ItemCode}_{l.ItemBarcode}", l => l);

                var linesToUpdate = new List<PoLine>();
                var linesToInsert = new List<PoLine>();
                bool hasQuantityDecreases = false;

                foreach (var freshLine in freshData.Lines)
                {
                    var key = $"{freshLine.ItemCode}_{freshLine.ItemBarcode}";
                    if (existingLinesDict.TryGetValue(key, out var existingLine))
                    {
                        if (freshLine.OrderedQty < existingLine.OrderedQty)
                            hasQuantityDecreases = true;

                        UpdatePoLineFromFreshData(existingLine, freshLine);
                        linesToUpdate.Add(existingLine);
                    }
                    else
                    {
                        linesToInsert.Add(CreateNewPoLine(freshData.OrderNo, freshLine));
                    }
                }

                var linesToDelete = existingLines.Where(l => !freshLinesDict.ContainsKey($"{l.ItemCode}_{l.ItemBarcode}")).ToList();

                foreach (var line in linesToDelete)
                    conn.Delete(line);

                foreach (var line in linesToInsert)
                    conn.Insert(line);

                foreach (var line in linesToUpdate)
                    conn.Update(line);

                if (existingHeader != null)
                {
                    existingHeader.DueDate = freshData.DueDate;
                    existingHeader.Status = freshData.Status;
                    existingHeader.SupplierName = freshData.SupplierName;
                    existingHeader.JsonData = System.Text.Json.JsonSerializer.Serialize(freshData);
                    conn.Update(existingHeader);
                }
                else
                {
                    conn.Insert(new PoHeader
                    {
                        OrderNo = freshData.OrderNo,
                        DueDate = freshData.DueDate,
                        Status = freshData.Status,
                        SupplierName = freshData.SupplierName,
                        JsonData = System.Text.Json.JsonSerializer.Serialize(freshData),
                        iscompleted = false
                    });
                }
            }).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(freshData.OrderNo))
            {
                _poHeaderCache.TryRemove(freshData.OrderNo, out _);
                _cacheTimestamps.TryRemove($"poheader_{freshData.OrderNo}", out _);
            }
        }

       private void UpdatePoLineFromFreshData(PoLine existingLine, PurchaseOrderLine freshLine)
        {
            if (existingLine == null || freshLine == null)
                throw new ArgumentNullException("PoLine or PurchaseOrderLine is null.");

            // Check if quantity decreased - if so, zero out workflow progress
            if (freshLine.OrderedQty < existingLine.OrderedQty)
            {
                existingLine.ScanAcceptQty = 0;
                existingLine.ScanRejectQty = 0;
                existingLine.ReceivedQty = 0;
                existingLine.ReceivedString = null;
                existingLine.GRNum = null;
            }

            // Update with fresh data (preserves workflow if quantity did not decrease)
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
            if (freshLine == null)
                throw new ArgumentNullException(nameof(freshLine));

            return new PoLine
            {
                OrderNo = orderNo,  // Use the passed-in orderNo instead of freshLine.DocNum
                LineNo = freshLine.LineNo,
                ItemCode = freshLine.ItemCode,
                ItemDesc = freshLine.ItemDesc,
                ItemBarcode = freshLine.ItemBarcode,
                PackBarcode = freshLine.PackBarcode,
                PackSize = freshLine.PackSize,
                NoOfPacks = freshLine.no_of_packs,
                OrderedQty = freshLine.OrderedQty,
                ReceivedQty = 0,
                ScanAcceptQty = 0,
                ScanRejectQty = 0,
                BinLocation = freshLine.BinLocation,
                WhID = freshLine.WhID,
                GRNum = null,
                ReceivedString = null
            };
        }


        // Sales Order Operations
        public async Task SaveWarehousesAsync(List<Warehouse> warehouses)
        {
            if (warehouses == null)
                throw new ArgumentNullException(nameof(warehouses));

            _warehouseCache.Clear();
            _cacheTimestamps.Clear();

            await _dbConnection.RunInTransactionAsync(conn =>
            {
                conn.DeleteAll<Warehouse>();
                conn.InsertAll(warehouses);
            }).ConfigureAwait(false);
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

            if (_warehouseCache.TryGetValue(code, out var cachedWarehouse))
            {
                if (_cacheTimestamps.TryGetValue($"warehouse_{code}", out var timestamp) &&
                    DateTime.Now - timestamp < _cacheExpiration)
                {
                    return cachedWarehouse;
                }

                _warehouseCache.TryRemove(code, out _);
                _cacheTimestamps.TryRemove($"warehouse_{code}", out _);
            }

            var warehouse = await _dbConnection.Table<Warehouse>()
                .FirstOrDefaultAsync(w => w.Code == code)
                .ConfigureAwait(false);

            if (warehouse != null)
            {
                _warehouseCache.TryAdd(code, warehouse);
                _cacheTimestamps.TryAdd($"warehouse_{code}", DateTime.Now);
            }

            return warehouse;
        }

        // Get description by code (utility method) with caching
        public async Task<string?> GetWarehouseDescriptionAsync(string code)
        {
            var warehouse = await GetWarehouseByCodeAsync(code).ConfigureAwait(false);
            return warehouse?.Description;
        }

        // Delete all warehouses
        public Task<int> ClearWarehousesAsync()
        {
            _warehouseCache.Clear();
            _cacheTimestamps.Clear();
            return _dbConnection.DeleteAllAsync<Warehouse>();
        }

        // Check if there are any warehouses
        public async Task<bool> HasWarehousesAsync()
        {
            var count = await _dbConnection.Table<Warehouse>().CountAsync().ConfigureAwait(false);
            return count > 0;
        }

       public async Task<List<SoLine>> GetSoLinesByOrderNoAsync(string orderNo)
        {
            if (string.IsNullOrWhiteSpace(orderNo))
                return new List<SoLine>();

            orderNo = FixOrderNo(orderNo);

            return await _dbConnection.Table<SoLine>()
                                      .Where(line => line.DocNum == orderNo)
                                      .OrderBy(line => line.Id)
                                      .ToListAsync().ConfigureAwait(false);
        }

        public async Task UpdateSoLineAsync(SoLine soLine)
        {
            if (soLine == null)
                throw new ArgumentNullException(nameof(soLine));

            await _dbConnection.UpdateAsync(soLine).ConfigureAwait(false);
        }

        public async Task UpdateSoHeaderAsync(SoHeader soHeader)
        {
            if (soHeader?.Reference == null) return;

            await Task.Run(() =>
            {
                lock (_dbLock)
                {
                    try
                    {
                        using (var syncConn = new SQLiteConnection(_dbConnection.DatabasePath))
                        {
                            syncConn.RunInTransaction(() => syncConn.Update(soHeader));
                        }
                    }
                    catch { }
                }
            }).ConfigureAwait(false);
        }

        public async Task<SoHeader?> GetSoHeaderByOrderNoAsync(string orderNo)
        {
            if (string.IsNullOrWhiteSpace(orderNo))
                return null;

            orderNo = FixOrderNo(orderNo);

            if (_soHeaderCache.TryGetValue(orderNo, out var cachedHeader))
            {
                if (_cacheTimestamps.TryGetValue($"soheader_{orderNo}", out var timestamp) &&
                    DateTime.Now - timestamp < _cacheExpiration)
                {
                    return cachedHeader;
                }

                _soHeaderCache.TryRemove(orderNo, out _);
                _cacheTimestamps.TryRemove($"soheader_{orderNo}", out _);
            }

            var soHeader = await _dbConnection.Table<SoHeader>()
                .FirstOrDefaultAsync(h => h.Reference == orderNo)
                .ConfigureAwait(false);

            if (soHeader != null)
            {
                _soHeaderCache.TryAdd(orderNo, soHeader);
                _cacheTimestamps.TryAdd($"soheader_{orderNo}", DateTime.Now);
            }

            return soHeader;
        }

        public async Task DeleteSoAsync(string orderNo)
        {
            if (string.IsNullOrWhiteSpace(orderNo))
                return;

            orderNo = FixOrderNo(orderNo);

            await _dbConnection.RunInTransactionAsync(conn =>
            {
                conn.Table<SoLine>().Where(p => p.DocNum == orderNo).Delete();
                var soHeader = conn.Table<SoHeader>().FirstOrDefault(p => p.Reference == orderNo);
                if (soHeader != null)
                    conn.Delete(soHeader);
            }).ConfigureAwait(false);
        }

        public async Task DeleteAllExceptSoAsync(string orderNo)
        {
            if (string.IsNullOrWhiteSpace(orderNo))
                return;

            orderNo = FixOrderNo(orderNo);

            await _dbConnection.RunInTransactionAsync(conn =>
            {
                conn.Table<SoLine>().Where(p => p.DocNum != orderNo).Delete();
                var headersToDelete = conn.Table<SoHeader>().Where(p => p.Reference != orderNo).ToList();
                foreach (var header in headersToDelete)
                {
                    conn.Delete(header);
                }
            }).ConfigureAwait(false);
        }
        
        public async Task MergeSoDataAsync(SalesOrderResponse freshData)
        {
            if (freshData?.Lines == null || !freshData.Lines.Any())
                throw new ArgumentException("Invalid sales order data for merge.");
            bool hasQuantityDecreases = false;
            await _dbConnection.RunInTransactionAsync(async conn =>
            {
                var existingHeader = await GetSoHeaderByOrderNoAsync(freshData.Reference);
                var existingLines = await GetSoLinesByOrderNoAsync(freshData.Reference);

                // Header processing
                if (existingHeader != null)
                {
                    existingHeader.CustomerOrderNo = freshData.CustomerOrderNo;
                    existingHeader.CustomerName = freshData.CustomerName;
                    existingHeader.AreaDescription = freshData.AreaDescription;
                    existingHeader.DueDate = freshData.DueDate;
                    existingHeader.OrderStatus = freshData.OrderStatus;
                    existingHeader.JsonData = System.Text.Json.JsonSerializer.Serialize(freshData);
                    await UpdateAsync(existingHeader);
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

                // Line processing
                var existingLinesDict = existingLines.ToDictionary(l => $"{l.ItemCode}_{l.ItemBarcode}", l => l);
                var freshLinesDict = freshData.Lines.ToDictionary(l => $"{l.ItemCode}_{l.ItemBarcode}", l => l);

                var linesToUpdate = new List<SoLine>();
                var linesToInsert = new List<SoLine>();
               
                foreach (var freshLine in freshData.Lines)
                {
                    var key = $"{freshLine.ItemCode}_{freshLine.ItemBarcode}";
                    if (existingLinesDict.TryGetValue(key, out var existingLine))
                    {
                        if (freshLine.OrderedQty < existingLine.OrderedQty)
                            hasQuantityDecreases = true;

                        UpdateSoLineFromFreshData(existingLine, freshLine);
                        linesToUpdate.Add(existingLine);
                    }
                    else
                    {
                        linesToInsert.Add(CreateNewSoLine(freshData.Reference, freshLine));
                    }
                }

                var linesToDelete = existingLines.Where(l => !freshLinesDict.ContainsKey($"{l.ItemCode}_{l.ItemBarcode}")).ToList();

                // Batch operations
                foreach (var line in linesToDelete)
                    await DeleteAsync(line);

                if (linesToInsert.Any())
                    await InsertAllAsync(linesToInsert);

                UpdateWorkflowStatus(linesToUpdate.Concat(linesToInsert).ToList(), existingHeader);

                if (linesToUpdate.Any())
                    await UpdateAllAsync(linesToUpdate);

                // Cache invalidation
                if (!string.IsNullOrWhiteSpace(freshData.Reference))
                {
                    _soHeaderCache.TryRemove(freshData.Reference, out _);
                    _cacheTimestamps.TryRemove($"soheader_{freshData.Reference}", out _);
                }
            }).ConfigureAwait(false);

            // UI alerts outside transaction
            if (hasQuantityDecreases)
            {
                await Application.Current.MainPage.DisplayAlert("Quantities Reduced",
                    "Some item quantities have been reduced.", "OK");
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
            var hasIncompletePicking = false;
            var hasIncompletePacking = false;
            var hasIncompleteChecking = false;

            foreach (var line in lines)
            {
                line.Picked = line.PickedQty >= line.OrderedQty;
                line.Packed = line.PackedQty >= line.OrderedQty;
                line.Checked = line.CheckedQty >= line.OrderedQty;
                line.Authorized = line.AuthorizedQty >= line.OrderedQty;

                if (!line.Picked) hasIncompletePicking = true;
                if (!line.Packed) hasIncompletePacking = true;
                if (!line.Checked) hasIncompleteChecking = true;

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

            var allPicked = !hasIncompletePicking;
            var allPacked = !hasIncompletePacking;
            var allChecked = !hasIncompleteChecking;
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
                .FirstOrDefaultAsync(l => l.DocNum == orderNo && (l.ItemBarcode == barcode || l.PackBarcode == barcode)).ConfigureAwait(false);

            return line;
        }

        // Check if any user has started a phase at header level
        public async Task<bool> HasAnyUserStartedPhaseAsync(string orderNo, string phaseField)
        {
            var header = await GetSoHeaderByOrderNoAsync(orderNo).ConfigureAwait(false);
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
            var header = await GetSoHeaderByOrderNoAsync(orderNo).ConfigureAwait(false);
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
            var header = await GetSoHeaderByOrderNoAsync(orderNo).ConfigureAwait(false);
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
            var lines = await GetSoLinesByOrderNoAsync(orderNo).ConfigureAwait(false);
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
            var status = await GetPhaseStatusAsync(orderNo).ConfigureAwait(false);
            return status.HasLines && status.AllPicked;
        }
        public async Task<bool> AreAllLinesPackedAsync(string orderNo)
        {
            var status = await GetPhaseStatusAsync(orderNo).ConfigureAwait(false);
            return status.HasLines && status.AllPacked;
        }
        public async Task<bool> AreAllLinesCheckedAsync(string orderNo)
        {
            var status = await GetPhaseStatusAsync(orderNo).ConfigureAwait(false);
            return status.HasLines && status.AllChecked;
        }

        public async Task<bool> AreAllLinesAuthorizedAsync(string orderNo)
        {
            var status = await GetPhaseStatusAsync(orderNo).ConfigureAwait(false);
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
            await _dbConnection.ExecuteAsync("DROP INDEX IF EXISTS idx_unique_line");
            foreach (var item in items)
            {
                try
                {
                    // raw SQL so we control every byte
                    var sql = @"INSERT OR REPLACE INTO StockCountItem
                        (BatchNo, StockCode, StockItemIsActive, ProductGroup, StockCategory,
                         StockDescription, BarCode, BarcodeLmmp, WarehouseCode,
                         Pack, Level, Count1Qty, Count2Qty, CountBy,
                         ConfirmCountQty, ConfirmBy, CountComplete, CountString,
                         Phase1Complete, Phase2Complete)
                        VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?,?);";

                    var rows = await _dbConnection.ExecuteAsync(sql,
                        item.BatchNo,
                        item.StockCode,
                        item.StockItemIsActive,
                        item.ProductGroup,
                        item.StockCategory,
                        item.StockDescription,
                        item.BarCode,
                        item.BarcodeLmmp,
                        item.WarehouseCode,
                        item.Pack,
                        item.Level,
                        item.Count1Qty,
                        item.Count2Qty,
                        item.CountBy,
                        item.ConfirmCountQty,
                        item.ConfirmBy,
                        item.CountComplete,
                        item.CountString,
                        item.Phase1Complete,
                        item.Phase2Complete);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"FAILED BatchNo={item.BatchNo}, StockCode={item.StockCode} → {ex.Message}");
                }
            }

        }
        public async Task<List<StockCountItem>> GetStockCountItemsAsync()
        {
            return await _dbConnection.Table<StockCountItem>().ToListAsync().ConfigureAwait(false);
        }
        public async Task<StockCountItem?> GetStockCountItemByCodeAsync(string stockCode)
        {
            if (string.IsNullOrWhiteSpace(stockCode))
                return null;

            if (_stockCountItemCache.TryGetValue(stockCode, out var cachedItem))
            {
                if (_cacheTimestamps.TryGetValue($"stockcount_{stockCode}", out var timestamp) &&
                    DateTime.Now - timestamp < _cacheExpiration)
                {
                    return cachedItem;
                }

                _stockCountItemCache.TryRemove(stockCode, out _);
                _cacheTimestamps.TryRemove($"stockcount_{stockCode}", out _);
            }

            var stockCountItem = await _dbConnection.Table<StockCountItem>()
                .FirstOrDefaultAsync(item => item.StockCode == stockCode)
                .ConfigureAwait(false);

            if (stockCountItem != null)
            {
                _stockCountItemCache.TryAdd(stockCode, stockCountItem);
                _cacheTimestamps.TryAdd($"stockcount_{stockCode}", DateTime.Now);
            }

            return stockCountItem;
        }
        public async Task<StockCountItem?> GetStockCountItemByCodeAndBatchAsync(string stockCode, string batchNo)
        {
            return await _dbConnection.Table<StockCountItem>()
                .FirstOrDefaultAsync(item => item.StockCode == stockCode && item.BatchNo == batchNo).ConfigureAwait(false);
        }
        public async Task UpdateStockCountItemAsync(StockCountItem item)
        {
            // Use a more efficient update operation
            await _dbConnection.UpdateAsync(item).ConfigureAwait(false);

            // Invalidate cache for this item to ensure data consistency
            if (!string.IsNullOrWhiteSpace(item.StockCode))
            {
                _stockCountItemCache.TryRemove(item.StockCode, out _);
                _cacheTimestamps.TryRemove($"stockcount_{item.StockCode}", out _);
            }
        }

        public async Task<List<StockCountItem>> GetStockCountItemsByBatchAsync(string batchNo)
        {
            //Debug.WriteLine($"Querying database for batch: {batchNo}");
            var query = _dbConnection.Table<StockCountItem>().Where(item => item.BatchNo == batchNo);
            var list = await query.ToListAsync();
           // Debug.WriteLine($"Query returned {list.Count} items for batch {batchNo}");
            //foreach (var item in list)
            //{
            //    Debug.WriteLine($"Loaded item: BatchNo={item.BatchNo}, StockCode={item.StockCode}");
            //}
            return list;
        }
        public async Task<List<StockCountItem>> GetIncompleteStockCountsAsync()
        {
            return await _dbConnection.Table<StockCountItem>()
                .Where(item => !item.CountComplete)
                .ToListAsync().ConfigureAwait(false);
        }
        public async Task<StockCountItem?> ResolveStockCountItemByBarcodeAsync(string scannedBarcode)
        {
            if (string.IsNullOrWhiteSpace(scannedBarcode))
                return null;

            var stockCountItem = await _dbConnection.Table<StockCountItem>()
                .Where(item =>
                    (!string.IsNullOrWhiteSpace(item.BarCode) && item.BarCode.Equals(scannedBarcode, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(item.BarcodeLmmp) && item.BarcodeLmmp.Equals(scannedBarcode, StringComparison.OrdinalIgnoreCase)))
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);

            if (stockCountItem != null)
                return stockCountItem;

            var matchingStockItem = await _dbConnection.Table<StockItem>()
                .Where(item => item.alternate_bar_codes != null &&
                    item.alternate_bar_codes.Contains(scannedBarcode))
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);

            if (matchingStockItem != null)
            {
                return await _dbConnection.Table<StockCountItem>()
                    .FirstOrDefaultAsync(item => item.StockCode == matchingStockItem.stock_code)
                    .ConfigureAwait(false);
            }

            return null;
        }

        private static readonly SemaphoreSlim _transactionLock = new SemaphoreSlim(1, 1);

        public async Task RunInTransactionAsync(Action<SQLiteConnection> action)
        {
            // 1. Fail gracefully instead of throwing
            if (action == null || _dbConnection?.DatabasePath == null)
                return;

            await _transactionLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await Task.Run(() =>
                {
                    using (var syncConn = new SQLiteConnection(_dbConnection.DatabasePath))
                    {
                        // 2. Manual transaction with timeout
                        syncConn.BeginTransaction();
                        try
                        {
                            action(syncConn);
                            syncConn.Commit();
                        }
                        catch (SQLiteException ex) when (IsDatabaseLocked(ex))
                        {
                            syncConn.Rollback();
                            Thread.Sleep(100); // Brief pause
                            throw; // Or retry logic if needed
                        }
                        catch
                        {
                            syncConn.Rollback();
                            // 3. Suppress all other errors
                        }
                    }
                }).ConfigureAwait(false);
            }
            finally
            {
                _transactionLock.Release();
            }
        }

        private static bool IsDatabaseLocked(SQLiteException ex)
        {
            return ex.Result == SQLite3.Result.Busy ||
                   ex.Result == SQLite3.Result.Locked;
        }

        public static class WarehouseCache
        {
            private static readonly List<Warehouse> _cache = new();
            private static Task<List<Warehouse>>? _loading;

            public static Task<List<Warehouse>> GetAsync()
            {
                if (_cache.Any())               // already loaded
                    return Task.FromResult(_cache);

                _loading ??= LoadAsync();       // start once
                return _loading;
            }

            private static async Task<List<Warehouse>> LoadAsync()
            {
                var list = await App.Db.GetWarehousesAsync().ConfigureAwait(false);
                _cache.AddRange(list);
                return _cache;
            }
        }

        public static void ClearCaches()
        {
            _stockItemCache.Clear();
            _warehouseCache.Clear();
            _stockCountItemCache.Clear();
            _poHeaderCache.Clear();
            _soHeaderCache.Clear();
            _cacheTimestamps.Clear();
        }

    }
}
