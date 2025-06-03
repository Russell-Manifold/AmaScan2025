using SQLite;

namespace AmaScan.sqliteModels
{
    [Table("Warehouse")]
    public class Warehouse
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        public string? Code { get; set; }

        public string? Description { get; set; }
    }
}
