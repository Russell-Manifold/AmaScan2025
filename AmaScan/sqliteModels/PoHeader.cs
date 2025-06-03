using SQLite;

namespace AmaScan.sqliteModels
{
    public class PoHeader : IIdentifiable
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        public string? OrderNo { get; set; }
        public DateTime DueDate { get; set; }
        public string? Status { get; set; }
        public string? DNnumber { get; set; }
        public string? SuppInvNumber { get; set; }
        public string? JsonData { get; set; }
        public bool iscompleted { get; set; }
    }
}
