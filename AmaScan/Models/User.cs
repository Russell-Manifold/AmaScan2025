namespace Data.Model
{
    public class User
    {
        public string? UserName { get; set; }
        public string? RoleName { get; set; }
        public bool CanPick { get; set; }
        public bool CanPack { get; set; }
        public bool CanCheck { get; set; }
        public bool Admin { get; set; }
        public bool SuperUser { get; set; }
        public bool CanReceive { get; set; }
        public bool CanTransfer { get; set; }
        public bool CanAuthReceiving { get; set; }
        public bool CanAuthPicking { get; set; }
        public bool CanAuthTransfer { get; set; }
    }
}
