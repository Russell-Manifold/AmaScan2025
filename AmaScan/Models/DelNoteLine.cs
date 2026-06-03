namespace AmaScan.Classes
{
    public class DelNoteLine
    {
        public long LineNo { get; set; }
        public string ItemCode { get; set; }
        public string ItemDesc { get; set; }
        public string ItemBarcode { get; set; }
        public decimal OrderedQty { get; set; }
        public decimal CheckedQty { get; set; }
        public string CheckedBy { get; set; }
        public DateTime? CheckStartDateTime { get; set; }
        public DateTime? CheckCompleteDateTime { get; set; }
    }
}
