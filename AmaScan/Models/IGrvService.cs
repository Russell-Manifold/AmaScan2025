namespace AmaScan.Classes
{
    public interface IGrvService
    {
        Task<GrvResult> SendAsync(
            string poNumber,
            string supplierAccountCode,
            string branchCode,
            List<GrvLine> lines,
            GrvHeader header);
    }
}