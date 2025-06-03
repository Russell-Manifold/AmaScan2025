using SQLite;
using System;
using System.Collections.Generic;
using System.Text;

namespace Data.Model
{
    public class DeviceConfig
    {
        [AutoIncrement, PrimaryKey]
        public int ID { get; set; }
        public string? DefaultAccWH { get; set; }
        public string? DefaultRejWH { get; set; }
        public string? ConnectionS { get; set; }
        public bool PaperPickSlips { get; set; }
        public bool UseBins { get; set; }
        public bool UseZones { get; set; }
        public bool GRVActive { get; set; }
        public bool RepackActive { get; set; }
        public bool WhseTrfActive { get; set; }
        public bool CountActive { get; set; }
        public bool InvoiceActive { get; set; }
        public string? ReceiveUser { get; set; }
        public string? InvoiceUser { get; set; }
        public string? WhTrfUser { get; set; }
        public bool DeletePOLines { get; set; }
        public bool DeleteSOLines { get; set; }
        public string? DefaultAPI { get; set; }
        public bool AllowPartReceive { get; set; }
        public bool AuthoriseReceive{ get; set; }
    }
}
