using SQLite;
using System;
using System.Collections.Generic;
using System.Text;

namespace Data.Model
{
    public class TransferItem
    {
        [AutoIncrement, PrimaryKey]
        public int ID { get; set; }
        public string? ScanBarcode { get; set; }
        public string? ItemBarcode { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemDesc { get; set; }
        public int ItemQtyOut { get; set; }
        public int ItemQtyIn { get; set; }
        public int PickerUser { get; set; }
        public int AuthUser { get; set; }
        public DateTime PickDateTime { get; set; }
        public string? WH { get; set; }
        public string? Bin { get; set; }
        public int iTrfID { get; set; }
    }
}
