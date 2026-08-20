using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace AmaScan.Classes
{
    public class OmniDelNoteService : IDelNoteService
    {
        private readonly HttpClient _httpClient;

        public OmniDelNoteService()
        {
            _httpClient = new HttpClient();
        }

        public async Task<DelNoteResult> SendAsync(
            string reference,
            string customerBranchCode,
            string warehouseCode,
            string status,
            List<DelNoteLine> lines,
            DelNoteHeader header)
        {
            try
            {
                if (lines == null || !lines.Any(l => l.CheckedQty > 0))
                {
                    return new DelNoteResult
                    {
                        Success = false,
                        ErrorMessage = "No checked quantities to submit."
                    };
                }

                var payload = new
                {
                    reference,
                    customerBranchCode = customerBranchCode ?? "HO",
                    // Send the device "Main Store" as-is. Do NOT substitute a hardcoded default:
                    // the server stores whatever we send and later builds the Omni delivery note
                    // from it, so a wrong guess here would push an invalid warehouse to Omni.
                    warehouseCode = string.IsNullOrWhiteSpace(warehouseCode) ? "" : warehouseCode,
                    status = string.IsNullOrWhiteSpace(status) ? "Outstanding" : status,
                    header = header == null ? null : new
                    {
                        createdBy = header.CreatedBy,
                        createStartTime = header.CreateStartTime,
                        createEndTime = header.CreateEndTime,
                        deviceName = header.DeviceName,
                        totalLines = header.TotalLines,
                        checkedLines = header.CheckedLines,
                        discrepancyLines = header.DiscrepancyLines
                    },
                    lines = lines.Select(l => new
                    {
                        lineNo = l.LineNo,
                        itemCode = l.ItemCode,
                        itemDesc = l.ItemDesc,
                        itemBarcode = l.ItemBarcode,
                        orderedQty = l.OrderedQty,
                        checkedQty = l.CheckedQty,
                        checkedBy = l.CheckedBy,
                        checkStartDateTime = l.CheckStartDateTime,
                        checkCompleteDateTime = l.CheckCompleteDateTime
                    })
                };

                string json = JsonSerializer.Serialize(payload);
                string apiBaseUrl = Preferences.Get("ApiBaseUrl", AppConfig.ApiBaseUrl);
                string url = $"{apiBaseUrl}CheckSalesOrder";

                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content);
                string responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    string referenceNumber = "";

                    try
                    {
                        using var doc = JsonDocument.Parse(responseBody);
                        if (doc.RootElement.TryGetProperty("referenceNumber", out var refProp))
                            referenceNumber = refProp.GetString();
                    }
                    catch
                    {
                        referenceNumber = responseBody.Trim();
                    }

                    return new DelNoteResult
                    {
                        Success = true,
                        ReferenceNumber = referenceNumber
                    };
                }

                return new DelNoteResult
                {
                    Success = false,
                    ErrorMessage = responseBody
                };
            }
            catch (Exception ex)
            {
                return new DelNoteResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }
    }
}
