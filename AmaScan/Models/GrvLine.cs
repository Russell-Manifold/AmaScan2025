namespace AmaScan.Classes
{
    public class GrvLine
    {
        public long LineNo { get; set; }
        public string ItemCode { get; set; }
        public decimal ScanAcceptQty { get; set; }
        public decimal ScanRejectQty { get; set; }
        public decimal CostPrice { get; set; }
        public decimal CostPricePer { get; set; }
        public string VatCode { get; set; }
        public decimal VatRate { get; set; }
        public string AcceptWarehouseId { get; set; }
        public string RejectWarehouseId { get; set; }
    }
}