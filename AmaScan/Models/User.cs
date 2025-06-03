namespace Data.Model
{
    public class User
    {
        public string? UserName { get; set; }
        public string? RoleName { get; set; }
        public bool Picker { get; set; }
        public bool Packer { get; set; }
        public bool Checker { get; set; }
        public bool Supervisor { get; set; }
        public bool Manager { get; set; }
        public bool Admin { get; set; }
        public bool SuperUser { get; set; }
        public bool CanReceive { get; set; }
        public bool CanTransfer { get; set; }
        public bool CanAuthReceiving { get; set; }
        public bool CanAuthPicking { get; set; }
        public bool CanAuthTransfer { get; set; }
    }
}
