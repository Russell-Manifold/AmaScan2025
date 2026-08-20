using SQLite;

namespace AmaScan.sqliteModels
{
    public class PoHeader : IIdentifiable
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [Indexed]
        public string? OrderNo { get; set; }
        public string? SupplierName { get; set; }
        public string? AcctCode { get; set; }
        public string? BranchCode { get; set; }
        public DateTime DueDate { get; set; }
        public string? Status { get; set; }
        public string? DNnumber { get; set; }
        public string? SuppInvNumber { get; set; }
        public string? JsonData { get; set; }
        public bool iscompleted { get; set; }

        // Receiving audit fields
        public string? Receiver { get; set; }
        public DateTime? ReceiveStartTime { get; set; }
        public DateTime? ReceiveEndTime { get; set; }
        public string? Authorised { get; set; }
        public string? GrvNumber { get; set; }
        public string? DeviceName { get; set; }
        public int TotalLines { get; set; }
        public int ScannedLines { get; set; }
        public int DiscrepancyLines { get; set; }
    }
}
