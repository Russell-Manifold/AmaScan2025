using SQLite;
using System;
using System.Collections.Generic;
using System.Text;

namespace Data.Model
{
   public class TransferHeader
    {
        [AutoIncrement, PrimaryKey]
        public int ID { get; set; }
        public string? TrfDate { get; set; }
        public string? FromWH { get; set; }
        public string? ToWH { get; set; }
        public string? FromDate { get; set; }
        public string? RecDate { get; set; }
        public int PickerUser { get; set; }
        public int AuthUser { get; set; }
        public bool Active { get; set; }
    }
}
