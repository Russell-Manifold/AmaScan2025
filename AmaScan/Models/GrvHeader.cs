namespace AmaScan.Classes
{
    public class GrvHeader
    {
        public string? Receiver { get; set; }
        public DateTime? ReceiveStartTime { get; set; }
        public DateTime? ReceiveEndTime { get; set; }
        public string? Authorised { get; set; }
        public string? DeviceName { get; set; }

        // Captured on ReceivingDocumentsPage (at least one is required). Sent so the server can stamp
        // the Omni document's supplier_reference with what the receiver actually typed, instead of
        // letting it default to the purchase order's reference.
        public string? DeliveryNoteNumber { get; set; }
        public string? SupplierInvoiceNumber { get; set; }
        public int TotalLines { get; set; }
        public int ScannedLines { get; set; }
        public int DiscrepancyLines { get; set; }
    }
}
