using SQLite;

namespace AmaScan.sqliteModels
{
    public class StockItem
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        [Indexed(Name = "idx_item", Order = 1)]
        public string? stock_code { get; set; }
        public string? stock_description { get; set; }
        public string? unit_of_measure { get; set; }

        [Indexed(Name = "idx_item", Order = 2)]
        public string? alternate_bar_codes { get; set; }
        public decimal? pack { get; set; }   
        public string? active { get; set; }
        public decimal? unit_volume { get; set; }
        public decimal? unit_weight { get; set; }

        [Indexed(Name = "idx_item", Order = 3)]
        public string? bar_code { get; set; }

        [Indexed(Name = "idx_item", Order = 4)]
        public string? barcode_lmmp { get; set; }

        [Indexed(Name = "idx_item", Order = 5)]
        public string? bin_location { get; set; }
        public string? bin_location_description { get; set; }
        public string? stock_category_description { get; set; }
    }
}
