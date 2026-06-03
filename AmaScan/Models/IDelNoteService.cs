namespace AmaScan.Classes
{
    public interface IDelNoteService
    {
        Task<DelNoteResult> SendAsync(
            string reference,
            string customerBranchCode,
            string warehouseCode,
            string status,
            List<DelNoteLine> lines,
            DelNoteHeader header);
    }
}
