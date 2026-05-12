using System;
using System.Collections.Generic;
using System.Text;
using SQLite;

namespace Data.Model
{
    public class PurchaseOrderLine
    {
        [AutoIncrement, PrimaryKey]
        public int ID { get; set; }
        public long LineNo { get; set; }
        public string? OrderNo { get; set; }
        public string? DocNum { get; set; }
        public string? SupplierCode { get; set; }
        public string? SupplierName { get; set; }
        public string? ItemBarcode { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemDesc { get; set; }
        public int PackSize { get; set; }   
        public string? PackBarcode { get; set; }
        public int no_of_packs { get; set; }
        public decimal OrderedQty { get; set; }
        public decimal CostPrice { get; set; }
        public decimal CostPricePer { get; set; }
        public string? VatCode { get; set; }
        public decimal VatRate { get; set; }
        public int ScanAcceptQty { get; set; }
        public int ScanRejectQty { get; set; }
        public decimal Balance { get; set; }
        public string? Complete { get; set; }
        public int PalletNum { get; set; }
        public string? BinLocation { get; set; }
        public string? WhID { get; set; }
        public string? PurchasesGLAccount { get; set; }
        public bool GRN { get; set; }
    }
}
