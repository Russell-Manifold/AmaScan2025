using SQLite;
using System.ComponentModel;

namespace AmaScan.sqliteModels
{
    public class PoLine : IIdentifiable, INotifyPropertyChanged
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [Indexed(Name = "idx_po_line", Order = 1)]
        public string? OrderNo { get; set; }

        [Indexed(Name = "idx_po_lineNum", Order = 1)]
        public long LineNo { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemDesc { get; set; }

        [Indexed(Name = "idx_po_line", Order = 2)]
        public string? ItemBarcode { get; set; }
        public int PackSize { get; set; }

        [Indexed(Name = "idx_po_line", Order = 3)]
        public string? PackBarcode { get; set; }
        public int NoOfPacks { get; set; }
        public decimal OrderedQty { get; set; }
        public decimal ReceivedQty { get; set; }
        public decimal CostPrice { get; set; }
        public decimal CostPricePer { get; set; }
        public string? VatCode { get; set; }
        public decimal VatRate { get; set; }

        private decimal _scanAcceptQty;
        public decimal ScanAcceptQty
        {
            get => _scanAcceptQty;
            set
            {
                if (_scanAcceptQty != value)
                {
                    _scanAcceptQty = value;
                    OnPropertyChanged(nameof(ScanAcceptQty));
                    OnPropertyChanged(nameof(OutstandingQty));
                    OnPropertyChanged(nameof(StatusColor));
                }
            }
        }

        private decimal _scanRejectQty;
        public decimal ScanRejectQty
        {
            get => _scanRejectQty;
            set
            {
                if (_scanRejectQty != value)
                {
                    _scanRejectQty = value;
                    OnPropertyChanged(nameof(ScanRejectQty));
                    OnPropertyChanged(nameof(OutstandingQty));
                    OnPropertyChanged(nameof(StatusColor));
                }
            }
        }
        public string? BinLocation { get; set; }

        [Indexed(Name = "idx_po_line", Order = 4)]
        public string? WhID { get; set; }

        // Destination stores captured at scan time — per line, per bucket.
        // AcceptWhID = where the accepted qty goes, RejectWhID = where the rejected qty goes.
        public string? AcceptWhID { get; set; }
        public string? RejectWhID { get; set; }
        public string? GRNum { get; set; }

        private string? _receivedString;
        public string? ReceivedString
        {
            get => _receivedString;
            set
            {
                if (_receivedString != value)
                {
                    _receivedString = value;
                    OnPropertyChanged(nameof(ReceivedString));
                }
            }
        }
        public string CodeAndBarcode => $"Code: {ItemCode}; {ItemBarcode}";
        public decimal OutstandingQty => OrderedQty - (ScanAcceptQty + ScanRejectQty);

        public string StatusColor
        {
            get
            {
                if (ScanAcceptQty == 0 && ScanRejectQty == 0)
                    return "Transparent";

                var total = ScanAcceptQty + ScanRejectQty;
                if (total == OrderedQty)
                    return "#DFFFD6"; // pale green — exact match
                if (total > OrderedQty)
                    return "#FFB6C1"; // light pink — over-received

                return "#FFDAB9"; // pale orange — under-received
            }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}