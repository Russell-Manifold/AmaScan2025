using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace Data.Model
{

    public class WHtrfResponse
    {
        public object? Result { get; set; }
        public List<WHtrfFlatItem>? Value { get; set; }
    }

    public class WHtrfFlatItem
    {
        public string? requisition_number { get; set; }
        public DateTime due_date_and_time { get; set; }
        public string? source_warehouse { get; set; }
        public string? source_warehouse_description { get; set; }
        public string? destination_warehouse { get; set; }
        public string? destination_warehouse_description { get; set; }
        public string? stock_code { get; set; }
        public string? bar_code { get; set; }
        public string? stock_description { get; set; }
        public decimal? outstanding_qty_to_deliver { get; set; }
    }

    public class WHtrfRequestHeader
    {
        public string? requisition_number { get; set; }
        public DateTime due_date_and_time { get; set; }
        public string? source_warehouse { get; set; }
        public string? source_warehouse_description { get; set; }
        public string? destination_warehouse { get; set; }
        public string? destination_warehouse_description { get; set; }
        public List<WHtrfRequestLine> lines { get; set; } = new List<WHtrfRequestLine>();
    }

    public class WHtrfRequestLine
    {
        public string? stock_code { get; set; }
        public string? bar_code { get; set; }
        public string? stock_description { get; set; }
        public decimal? outstanding_qty_to_deliver { get; set; }
    }
}
