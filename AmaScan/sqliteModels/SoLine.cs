using SQLite;
using System.ComponentModel;
using Microsoft.Maui.Graphics;

namespace AmaScan.sqliteModels
{
    public class SoLine : IIdentifiable, INotifyPropertyChanged
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        public int SalesOrderHeaderId { get; set; }

        [Indexed(Name = "idx_so_line", Order = 1)]
        public string? DocNum { get; set; }

        [Indexed(Name = "idx_so_line", Order = 2)]
        public string? CustomerAccount { get; set; }
        public string? CustomerName { get; set; }

        [Indexed(Name = "idx_so_line", Order = 3)]
        public string? ItemCode { get; set; }
        public string? ItemDesc { get; set; }

        [Indexed(Name = "idx_so_line", Order = 4)]
        public string? ItemBarcode { get; set; }
        public int PackSize { get; set; }

        [Indexed(Name = "idx_so_line", Order = 5)]
        public string? PackBarcode { get; set; }
        public int NoOfPacks { get; set; }
        public decimal OrderedQty { get; set; }
        public decimal ScanAcceptQty { get; set; }
        public decimal Balance { get; set; }
        public string? Complete { get; set; }
        public string? Bin { get; set; }
        private bool _picked;
        public bool Picked
        {
            get => _picked;
            set
            {
                if (_picked != value)
                {
                    _picked = value;
                    OnPropertyChanged(nameof(Picked));
                    OnPropertyChanged(nameof(StatusColor));
                }
            }
        }

        // Picking Phase
        private decimal _pickedQty;
        public decimal PickedQty
        {
            get => _pickedQty;
            set
            {
                if (_pickedQty != value)
                {
                    _pickedQty = value;
                    OnPropertyChanged(nameof(PickedQty));
                    OnPropertyChanged(nameof(OutstandingPickingQty));
                    OnPropertyChanged(nameof(StatusColor));
                    OnPropertyChanged(nameof(DiscrepancyStatus));
                    OnPropertyChanged(nameof(HasDiscrepancy));
                    OnPropertyChanged(nameof(ShowDiscrepancyStatus));
                }
            }
        }
        public string? PickedBy { get; set; }
        public DateTime? PickStartDateTime { get; set; }
        public DateTime? PickCompleteDateTime { get; set; }

        [Indexed(Name = "idx_so_line", Order = 6)]
        public bool PickStarted { get; set; }

        // Packing Phase
        private decimal _packedQty;
        public decimal PackedQty
        {
            get => _packedQty;
            set
            {
                if (_packedQty != value)
                {
                    _packedQty = value;
                    OnPropertyChanged(nameof(PackedQty));
                    OnPropertyChanged(nameof(PackingOutstandingQty));
                    OnPropertyChanged(nameof(StatusColor));
                    OnPropertyChanged(nameof(DiscrepancyStatus));
                    OnPropertyChanged(nameof(HasDiscrepancy));
                    OnPropertyChanged(nameof(ShowDiscrepancyStatus));
                }
            }
        }
        public string? PackedBy { get; set; }
        public DateTime? PackStartDateTime { get; set; }
        public DateTime? PackCompleteDateTime { get; set; }
        public bool PackStarted { get; set; }

        private bool _packed;
        public bool Packed
        {
            get => _packed;
            set
            {
                if (_packed != value)
                {
                    _packed = value;
                    OnPropertyChanged(nameof(Packed));
                    OnPropertyChanged(nameof(StatusColor));
                }
            }
        }

        // Checking Phase
        private decimal _checkedQty;
        public decimal CheckedQty
        {
            get => _checkedQty;
            set
            {
                if (_checkedQty != value)
                {
                    _checkedQty = value;
                    OnPropertyChanged(nameof(CheckedQty));
                    OnPropertyChanged(nameof(CheckingOutstandingQty));
                    OnPropertyChanged(nameof(StatusColor));
                    OnPropertyChanged(nameof(DiscrepancyStatus));
                    OnPropertyChanged(nameof(HasDiscrepancy));
                    OnPropertyChanged(nameof(ShowDiscrepancyStatus));
                }
            }
        }
        public string? CheckedBy { get; set; }
        public DateTime? CheckStartDateTime { get; set; }
        public DateTime? CheckCompleteDateTime { get; set; }
        public bool CheckStarted { get; set; }

        private bool _checked;
        public bool Checked
        {
            get => _checked;
            set
            {
                if (_checked != value)
                {
                    _checked = value;
                    OnPropertyChanged(nameof(Checked));
                    OnPropertyChanged(nameof(StatusColor));
                }
            }
        }

        // Authorization Phase
        private decimal _authorizedQty;
        public decimal AuthorizedQty
        {
            get => _authorizedQty;
            set
            {
                if (_authorizedQty != value)
                {
                    _authorizedQty = value;
                    OnPropertyChanged(nameof(AuthorizedQty));
                    OnPropertyChanged(nameof(AuthorizationOutstandingQty));
                    OnPropertyChanged(nameof(StatusColor));
                    OnPropertyChanged(nameof(DiscrepancyStatus));
                    OnPropertyChanged(nameof(HasDiscrepancy));
                    OnPropertyChanged(nameof(ShowDiscrepancyStatus));
                }
            }
        }
        public string? AuthorizedBy { get; set; }
        public DateTime? AuthStartDateTime { get; set; }
        public DateTime? AuthCompleteDateTime { get; set; }
        public bool AuthStarted { get; set; }

        private bool _authorized;
        public bool Authorized
        {
            get => _authorized;
            set
            {
                if (_authorized != value)
                {
                    _authorized = value;
                    OnPropertyChanged(nameof(Authorized));
                    OnPropertyChanged(nameof(StatusColor));
                }
            }
        }

        public string? PickedString { get; set; }

        public decimal OutstandingPickingQty => OrderedQty - PickedQty;

        [Ignore]
        public decimal PackingOutstandingQty => PickedQty - PackedQty;

        [Ignore]
        public decimal CheckingOutstandingQty => PackedQty - CheckedQty;

        [Ignore]
        public decimal AuthorizationOutstandingQty => CheckedQty - AuthorizedQty;

        [Ignore]
        public bool HasDiscrepancy
        {
            get
            {
                // Don't consider it a discrepancy if nothing has been picked yet
                if (PickedQty == 0)
                    return false;

                // Check picking discrepancy (picked vs ordered)
                if (PickedQty != OrderedQty)
                    return true;

                // Check packing discrepancy (packed vs picked) - only if packing has started
                if (PackedQty > 0 && PackedQty != PickedQty)
                    return true;

                // Check checking discrepancy (checked vs packed) - only if checking has started
                if (CheckedQty > 0 && CheckedQty != PackedQty)
                    return true;

                // Check authorization discrepancy (authorized vs checked) - only if authorization has started
                if (AuthorizedQty > 0 && AuthorizedQty != CheckedQty)
                    return true;

                return false;
            }
        }

        [Ignore]
        public string DiscrepancyStatus
        {
            get
            {
                if (PickedQty != OrderedQty)
                    return $"Picking Discrepancy: {PickedQty} vs {OrderedQty}";
                if (PackedQty > 0 && PackedQty != PickedQty)
                    return $"Packing Discrepancy: {PackedQty} vs {PickedQty}";
                if (CheckedQty > 0 && CheckedQty != PackedQty)
                    return $"Checking Discrepancy: {CheckedQty} vs {PackedQty}";
                if (AuthorizedQty > 0 && AuthorizedQty != CheckedQty)
                    return $"Authorization Discrepancy: {AuthorizedQty} vs {CheckedQty}";
                return string.Empty;
            }
        }

        [Ignore]
        public bool ShowDiscrepancyStatus
        {
            get
            {
                // Show discrepancy if there's any discrepancy and the item has been started
                return HasDiscrepancy && PickedQty > 0;
            }
        }

        [Ignore]
        public string StatusColor
        {
            get
            {
                // Not started
                if (PickedQty == 0)
                    return "Transparent";

                // Partially picked
                if (PickedQty < OrderedQty)
                    return "LightYellow";

                // Fully picked but not packed
                if (PickedQty >= OrderedQty && PackedQty < PickedQty)
                    return "PapayaWhip";

                // Fully packed but not checked
                if (PackedQty >= PickedQty && CheckedQty < PackedQty)
                    return "LightBlue";

                // Fully checked but not authorized
                if (CheckedQty >= PackedQty && AuthorizedQty < CheckedQty)
                    return "Honeydew";

                // Completed (all phases done)
                if (AuthorizedQty >= CheckedQty)
                    return "LightGreen";

                return "Transparent";
            }
        }        

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}