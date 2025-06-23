using System;

namespace Data.Model
{
    public class SalesOrderLine
    {
        public int Id { get; set; }
        public int SalesOrderHeaderId { get; set; }
        public string? DocNum { get; set; }
        public string? CustomerAccount { get; set; }
        public string? CustomerName { get; set; }
        public string? ItemBarcode { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemDesc { get; set; }
        public int PackSize { get; set; }
        public string? PackBarcode { get; set; }
        public int NoOfPacks { get; set; }
        public decimal OrderedQty { get; set; }
        public decimal ScanAcceptQty { get; set; }
        public decimal Balance { get; set; }
        public string? Complete { get; set; }
        public string? Bin { get; set; }
        public bool Picked { get; set; }

        // Picking Phase Tracking
        public decimal PickedQty { get; set; }
        public string? PickedBy { get; set; }
        public DateTime? PickStartDateTime { get; set; }
        public DateTime? PickCompleteDateTime { get; set; }

        // Packing Phase Tracking
        public decimal PackedQty { get; set; }
        public string? PackedBy { get; set; }
        public DateTime? PackStartDateTime { get; set; }
        public DateTime? PackCompleteDateTime { get; set; }

        // Checking Phase Tracking
        public decimal CheckedQty { get; set; }
        public string? CheckedBy { get; set; }
        public DateTime? CheckStartDateTime { get; set; }
        public DateTime? CheckCompleteDateTime { get; set; }

        // Authorization Phase Tracking
        public decimal AuthorizedQty { get; set; }
        public string? AuthorizedBy { get; set; }
        public DateTime? AuthStartDateTime { get; set; }
        public DateTime? AuthCompleteDateTime { get; set; }
    }
}