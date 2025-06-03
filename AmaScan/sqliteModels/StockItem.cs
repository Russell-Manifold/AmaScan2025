using SQLite;

namespace AmaScan.sqliteModels
{
    public class StockItem
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
        public string? stock_code { get; set; }
        public string? stock_description { get; set; }
        public string? unit_of_measure { get; set; }
        public string? alternate_bar_codes { get; set; }
        public decimal? pack { get; set; }   
        public string? active { get; set; }
        public decimal? unit_volume { get; set; }
        public decimal? unit_weight { get; set; }
        public string? bar_code { get; set; }
        public string? barcode_lmmp { get; set; }
        public string? bin_location { get; set; }
        public string? bin_location_description { get; set; }
        public string? stock_category_description { get; set; }
    }
}
