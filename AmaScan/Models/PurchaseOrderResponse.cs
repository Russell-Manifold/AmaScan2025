namespace Data.Model
{
    public class PurchaseOrderResponse
    {
        public string? SupplierCode { get; set; }
        public string? SupplierName { get; set; }
        public string? BranchCode { get; set; }
        public string? OrderNo { get; set; }
        public string? Status { get; set; }
        public string? DNnumber { get; set; }
        public string? SuppInvNumber { get; set; }

        public DateTime DueDate { get; set; }
        public List<PurchaseOrderLine>? Lines { get; set; }
    }
}
