using AmaScan.sqliteModels;
using Newtonsoft.Json;
using System.Text;

namespace AmaScan.Classes
{
    /// <summary>
    /// Posts a completed picking or packing phase to api/UpdateSalesOrderStage.
    ///
    /// This exists because the phone's sqlite copy used to be the ONLY place a picked/packed
    /// quantity lived — picking told the server nothing but "done" at header level. A checker on a
    /// second device (or the same device after a login, which drops the local SoLine table) then
    /// downloaded the order with PickedQty = 0 and could not check anything against it.
    ///
    /// The server also stamps the header Picked/Packed flags from this call, so the flag matrix
    /// lives in one place — see UpdateSalesOrderStageController.
    /// </summary>
    public static class StageSyncService
    {
        public const string StagePicking = "Picking";
        public const string StagePacking = "Packing";

        public class StageSyncResult
        {
            public bool Success { get; set; }
            public string ErrorMessage { get; set; }
        }

        public static async Task<StageSyncResult> SendAsync(string reference, string stage, List<SoLine> soLines)
        {
            if (soLines == null || soLines.Count == 0)
                return new StageSyncResult { Success = false, ErrorMessage = "No lines to submit." };

            bool isPicking = stage == StagePicking;

            try
            {
                var payload = new
                {
                    reference,
                    stage,
                    lines = soLines.Select(l => new
                    {
                        lineNo = l.SoLLineNo,
                        itemCode = l.ItemCode,
                        qty = isPicking ? l.PickedQty : l.PackedQty,
                        completedBy = isPicking ? l.PickedBy : l.PackedBy,
                        startDateTime = isPicking ? l.PickStartDateTime : l.PackStartDateTime,
                        completeDateTime = isPicking ? l.PickCompleteDateTime : l.PackCompleteDateTime
                    })
                };

                string json = JsonConvert.SerializeObject(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                using var client = new HttpClient();
                string url = $"{Preferences.Get("ApiBaseUrl", AppConfig.ApiBaseUrl)}UpdateSalesOrderStage";
                var response = await client.PostAsync(url, content);
                string body = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                    return new StageSyncResult { Success = true };

                System.Diagnostics.Debug.WriteLine($"UpdateSalesOrderStage ({stage}) failed: {response.StatusCode} - {body}");
                return new StageSyncResult { Success = false, ErrorMessage = body };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"StageSyncService.SendAsync ({stage}) error: {ex.Message}");
                return new StageSyncResult { Success = false, ErrorMessage = ex.Message };
            }
        }
    }
}
