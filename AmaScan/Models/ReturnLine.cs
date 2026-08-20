using SQLite;

namespace AmaScan.sqliteModels
{
    public class ReturnLine : IIdentifiable
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        public string? OrderNumber { get; set; }
        public string? ItemBarcode { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemDesc { get; set; }
        public decimal ReturnQty { get; set; }
        public bool Processed { get; set; }
        public DateTime ProcessedDateTime { get; set; }
        public string? WarehouseCode { get; set; }
        public string? ProcessedBy { get; set; }
        public string? ReturnReason { get; set; }
    }
}