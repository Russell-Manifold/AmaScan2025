using SQLite;
using System;

namespace AmaScan.sqliteModels
{
    public class SoHeader : IIdentifiable
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        public int SOrderID { get; set; }
        public string? CustomerOrderNo { get; set; }
        public string? CustomerAccount { get; set; }
        public string? CustomerName { get; set; }
        public string? AreaDescription { get; set; }
        public DateTime DueDate { get; set; }
        public string? OrderStatus { get; set; }
        public string? Reference { get; set; }
        public string? JsonData { get; set; }

        // Workflow Tracking at Header Level
        public string? Picker { get; set; }
        public bool PickStarted { get; set; }
        public bool Picked { get; set; }
        public bool Packed { get; set; }
        public bool Checked { get; set; }
        public bool Authed { get; set; }
        public double? Sequence { get; set; }

        // Header-level user tracking for single user per phase
        public string Packer { get; set; }
        public string Checker { get; set; }
        public string Authorizer { get; set; }

    }
}