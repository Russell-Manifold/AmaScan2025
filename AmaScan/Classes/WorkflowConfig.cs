using AmaScan.sqliteModels;
using Newtonsoft.Json.Linq;

namespace AmaScan.Classes
{
    /// <summary>
    /// Company-wide fulfilment workflow switch, set on the web Company Config page and served
    /// by api/WorkflowConfig. Checking is always the final stage and has no flag, so the four
    /// supported workflows are Pick→Pack→Check, Pick→Check, Pack→Check and Check only.
    ///
    /// The value is cached in Preferences and refreshed at login. If the API is unreachable the
    /// LAST KNOWN value is kept — the scanner is used offline in the warehouse, and silently
    /// dropping back to "Check only" would let an order skip a stage the company actually runs.
    /// A device that has never reached the API defaults to Check only, the pre-existing behaviour.
    /// </summary>
    public static class WorkflowConfig
    {
        private const string UsePickingKey = "WorkflowUsePicking";
        private const string UsePackingKey = "WorkflowUsePacking";

        public static bool UsePicking => Preferences.Get(UsePickingKey, false);
        public static bool UsePacking => Preferences.Get(UsePackingKey, false);

        /// <summary>
        /// Fetches the workflow from the API and caches it. Returns false when the fetch failed,
        /// in which case the cached value is untouched. Never throws.
        /// </summary>
        public static async Task<bool> RefreshAsync()
        {
            try
            {
                using var client = AppConfig.GetHttpClient();
                var response = await client.GetAsync("WorkflowConfig");

                if (!response.IsSuccessStatusCode)
                    return false;

                string json = await response.Content.ReadAsStringAsync();

                // Require both flags to be PRESENT in the body before caching anything. Deserialising
                // straight into the DTO would turn any body missing them into false/false and write
                // that to Preferences — silently downgrading the device to Check-only, which is the
                // exact outcome the "keep last known" design exists to prevent. Check-only is the one
                // mode where checking writes to the pick/pack columns, so guessing it is destructive.
                var parsed = JObject.Parse(json);
                var usePicking = parsed.GetValue(nameof(WorkflowConfigDto.UsePicking), StringComparison.OrdinalIgnoreCase);
                var usePacking = parsed.GetValue(nameof(WorkflowConfigDto.UsePacking), StringComparison.OrdinalIgnoreCase);

                if (usePicking == null || usePacking == null)
                {
                    System.Diagnostics.Debug.WriteLine($"WorkflowConfig.RefreshAsync: unusable body, keeping cached value. Body: {json}");
                    return false;
                }

                Preferences.Set(UsePickingKey, usePicking.Value<bool>());
                Preferences.Set(UsePackingKey, usePacking.Value<bool>());
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WorkflowConfig.RefreshAsync failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Quantity the PACKING stage measures against — whatever picking handed over, or the
        /// ordered qty when there is no picking stage (PickedQty would be 0 and nothing could
        /// ever be packed).
        /// </summary>
        public static decimal PackBasisQty(SoLine line) =>
            UsePicking ? line.PickedQty : line.OrderedQty;

        /// <summary>
        /// Quantity the CHECKING stage measures against — whatever the last active stage handed
        /// over, or the ordered qty under Check-only.
        /// </summary>
        public static decimal CheckBasisQty(SoLine line) =>
            UsePacking ? line.PackedQty
            : UsePicking ? line.PickedQty
            : line.OrderedQty;

        /// <summary>Human-readable workflow, for the dashboard/settings display.</summary>
        public static string Describe()
        {
            if (UsePicking && UsePacking) return "Pick → Pack → Check";
            if (UsePicking) return "Pick → Check";
            if (UsePacking) return "Pack → Check";
            return "Check only";
        }

        private class WorkflowConfigDto
        {
            public bool UsePicking { get; set; }
            public bool UsePacking { get; set; }
        }
    }
}
