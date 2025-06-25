using AmaScan.sqliteModels;
using Data.Model;

namespace AmaScan.Classes
{
    public static class ReceivingSession
    {
        public static PoHeader? CurrentPoHeader { get; set; }
        public static string? DeliveryNote { get; set; }
        public static string? SupplierInvoice { get; set; }
    }
}
