namespace AmaScan.Classes
{
    public enum GrvDocumentType
    {
        SupplierInvoice,
        DeliveryNote
    }

    public class GrvResult
    {
        public bool Success { get; set; }
        public string ReferenceNumber { get; set; }
        public GrvDocumentType DocumentType { get; set; }
        public string ErrorMessage { get; set; }
    }
}