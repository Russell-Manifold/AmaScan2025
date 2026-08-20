using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace AmaScan.Classes
{
    public class OmniGrvService : IGrvService
    {
        private readonly HttpClient _httpClient;

        public OmniGrvService()
        {
            _httpClient = new HttpClient();
        }

        public async Task<GrvResult> SendAsync(
            string poNumber,
            string supplierAccountCode,
            string branchCode,
            List<GrvLine> lines,
            GrvHeader header)
        {
            try
            {
                if (lines == null || !lines.Any(l => l.ScanAcceptQty > 0 || l.ScanRejectQty > 0))
                {
                    return new GrvResult
                    {
                        Success = false,
                        ErrorMessage = "No quantities to submit."
                    };
                }

                var payload = new
                {
                    poNumber,
                    supplierAccountCode,
                    branchCode = branchCode ?? "HO",
                    header = header == null ? null : new
                    {
                        receiver = header.Receiver,
                        receiveStartTime = header.ReceiveStartTime,
                        receiveEndTime = header.ReceiveEndTime,
                        authorised = header.Authorised,
                        deviceName = header.DeviceName,
                        deliveryNoteNumber = header.DeliveryNoteNumber,
                        supplierInvoiceNumber = header.SupplierInvoiceNumber,
                        totalLines = header.TotalLines,
                        scannedLines = header.ScannedLines,
                        discrepancyLines = header.DiscrepancyLines
                    },
                    lines = lines.Select(l => new
                    {
                        lineNo = l.LineNo,
                        itemCode = l.ItemCode,
                        scanAcceptQty = l.ScanAcceptQty,
                        scanRejectQty = l.ScanRejectQty,
                        costPrice = l.CostPrice,
                        costPricePer = l.CostPricePer,
                        vatCode = l.VatCode ?? "1",
                        vatRate = l.VatRate,
                        acceptWarehouseId = l.AcceptWarehouseId,
                        rejectWarehouseId = l.RejectWarehouseId
                    })
                };

                string json = JsonSerializer.Serialize(payload);
                string apiBaseUrl = Preferences.Get("ApiBaseUrl", AppConfig.ApiBaseUrl);
                string url = $"{apiBaseUrl}ProcessReceiving";

                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content);
                string responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    string referenceNumber = "";
                    GrvDocumentType docType = GrvDocumentType.SupplierInvoice;

                    try
                    {
                        using var doc = JsonDocument.Parse(responseBody);
                        // The server returns "invoiceNumber" when the company is configured to raise a
                        // Supplier Invoice and "referenceNumber" for a Supplier Delivery Note. Reading
                        // only one left the GRV number blank (and unsaved) for invoice companies.
                        if (doc.RootElement.TryGetProperty("referenceNumber", out var refProp))
                            referenceNumber = refProp.GetString();
                        else if (doc.RootElement.TryGetProperty("invoiceNumber", out var invProp))
                            referenceNumber = invProp.GetString();
                    }
                    catch
                    {
                        referenceNumber = responseBody.Trim();
                    }

                    referenceNumber = referenceNumber?.Trim() ?? "";

                    // "D" prefix indicates a delivery note
                    if (referenceNumber.StartsWith("D"))
                        docType = GrvDocumentType.DeliveryNote;

                    return new GrvResult
                    {
                        Success = true,
                        ReferenceNumber = referenceNumber,
                        DocumentType = docType
                    };
                }

                return new GrvResult
                {
                    Success = false,
                    ErrorMessage = responseBody
                };
            }
            catch (Exception ex)
            {
                return new GrvResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }
    }
}