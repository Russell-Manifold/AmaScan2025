namespace AmaScan.Classes
{
    public interface IGrvService
    {
        Task<GrvResult> SendAsync(
            string poNumber,
            string supplierAccountCode,
            string branchCode,
            string rejectWarehouseCode,
            List<GrvLine> lines,
            GrvHeader header);
    }
}