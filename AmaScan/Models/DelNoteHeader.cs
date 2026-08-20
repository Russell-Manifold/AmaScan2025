namespace AmaScan.Classes
{
    public class DelNoteHeader
    {
        public string? CreatedBy { get; set; }
        public DateTime? CreateStartTime { get; set; }
        public DateTime? CreateEndTime { get; set; }
        public string? DeviceName { get; set; }
        public int TotalLines { get; set; }
        public int CheckedLines { get; set; }
        public int DiscrepancyLines { get; set; }
    }
}
