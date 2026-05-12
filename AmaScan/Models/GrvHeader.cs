namespace AmaScan.Classes
{
    public class GrvHeader
    {
        public string? Receiver { get; set; }
        public DateTime? ReceiveStartTime { get; set; }
        public DateTime? ReceiveEndTime { get; set; }
        public string? Authorised { get; set; }
        public string? DeviceName { get; set; }
        public int TotalLines { get; set; }
        public int ScannedLines { get; set; }
        public int DiscrepancyLines { get; set; }
    }
}
