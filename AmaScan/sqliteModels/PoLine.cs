using SQLite;
using System.ComponentModel;

namespace AmaScan.sqliteModels
{
    public class PoLine: IIdentifiable, INotifyPropertyChanged
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        public string? OrderNo { get; set; }
        public long LineNo { get; set; }
        public string? ItemCode { get; set; }
        public string? ItemDesc { get; set; }
        public string? ItemBarcode { get; set; }
        public int PackSize { get; set; }
        public string? PackBarcode { get; set; }
        public int NoOfPacks { get; set; }
        public decimal OrderedQty { get; set; }
        public decimal ReceivedQty { get; set; }
        //public decimal ScanAcceptQty { get; set; }
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
        //public decimal ScanRejectQty { get; set; }
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
        public string? WhID { get; set; }
        public string? GRNum { get; set; }
        //public string? ReceivedString { get; set; }
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
        public string CodeAndBarcode => $"Code: {ItemCode}; Barcode: {ItemBarcode}";
        public decimal OutstandingQty => OrderedQty - (ScanAcceptQty + ScanRejectQty);
       
        public string StatusColor
        {
            get
            {
                var outstanding = OrderedQty - (ScanAcceptQty + ScanRejectQty);

                if (ScanAcceptQty == 0 && ScanRejectQty == 0)
                    return "Transparent"; // Clear if unstarted

                if (outstanding > 0)
                    return "#FFEFD5"; // Pale orange (PapayaWhip)

                return "#DFFFD6"; // Pale green
            }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
