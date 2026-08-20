using Data.Model;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AmaScan.Models
{
    public class TransferLineViewModel : INotifyPropertyChanged
    {
        private int _scannedQty;

        public WHtrfRequestLine Line { get; }
        public string StockDescription => Line.stock_description;
        public string StockCode => Line.stock_code;
        public string BarCode => Line.bar_code;
        public int OutstandingQty => Convert.ToInt32(Line.outstanding_qty_to_deliver);

        public int ScannedQty
        {
            get => _scannedQty;
            set
            {
                if (_scannedQty != value)
                {
                    _scannedQty = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ScannedQty)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsComplete)));
                }
            }
        }

        public bool IsComplete => ScannedQty == (int)OutstandingQty;

        public TransferLineViewModel(WHtrfRequestLine line)
        {
            Line = line;
            _scannedQty = 0;
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
