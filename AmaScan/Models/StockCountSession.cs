using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AmaScan.Models
{
    public class StockCountSession : INotifyPropertyChanged
    {
        private string _batchNo;
        private int _totalItems;
        private int _completedItems;
        private bool _hasCompletedItems;

        public string BatchNo
        {
            get => _batchNo;
            set
            {
                if (_batchNo != value)
                {
                    _batchNo = value;
                    OnPropertyChanged();
                }
            }
        }

        public int TotalItems
        {
            get => _totalItems;
            set
            {
                if (_totalItems != value)
                {
                    _totalItems = value;
                    OnPropertyChanged();
                }
            }
        }

        public int CompletedItems
        {
            get => _completedItems;
            set
            {
                if (_completedItems != value)
                {
                    _completedItems = value;
                    OnPropertyChanged();
                    HasCompletedItems = _completedItems > 0;
                }
            }
        }

        public bool HasCompletedItems
        {
            get => _hasCompletedItems;
            set
            {
                if (_hasCompletedItems != value)
                {
                    _hasCompletedItems = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(UploadButtonColor));
                }
            }
        }

        public Color UploadButtonColor
        {
            get => HasCompletedItems ? Color.FromHex("#007AFF") : Color.FromHex("#CCCCCC");
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string name = "") =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
} 