using Newtonsoft.Json;
using System.Collections.Generic;
using SQLite;

namespace AmaScan.sqliteModels
{
    public class StockCountItem : IIdentifiable
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        
        
        [Indexed(Name = "idx_sc_item", Order = 1)]
        public string BatchNo { get; set; }
        public string StockItemIsActive { get; set; }
        public string ProductGroup { get; set; }
        public string StockCategory { get; set; }

        [Indexed(Name = "idx_sc_item", Order = 2)]
        public string StockCode { get; set; }
        public string StockDescription { get; set; }

        [Indexed(Name = "idx_sc_item", Order = 3)]
        public string BarCode { get; set; }

        [Indexed(Name = "idx_sc_item", Order = 4)]
        public string BarcodeLmmp { get; set; }
        public string WarehouseCode { get; set; }
        public int Pack { get; set; }
        public decimal Level { get; set; }
        public decimal Count1Qty { get; set; }
        public decimal Count2Qty { get; set; }
        public string CountBy { get; set; }
        public decimal ConfirmCountQty { get; set; }
        public string ConfirmBy { get; set; }
        public bool CountComplete { get; set; }
        public bool CountString { get; set; }
        
        // Phase completion tracking
        public bool Phase1Complete { get; set; }
        public bool Phase2Complete { get; set; }

        public string StatusColor
        {
            get
            {
                return CountComplete ? "LightGreen" : "White";
            }
        }

        public string CompleteDisplay
        {
            get
            {
                return CountComplete ? "Yes" : "No";
            }
        }
    }
} 