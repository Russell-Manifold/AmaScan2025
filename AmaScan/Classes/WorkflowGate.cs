using Data.Model;

namespace AmaScan.Classes
{
    /// <summary>
    /// Blocks a stage from starting on an order whose previous stage is not finished, using the
    /// server's header flags rather than the local copy (the local copy may be a fresh download
    /// that has never seen the earlier stage).
    ///
    /// Which stage must be finished depends on the company's workflow:
    ///   Packing  needs picking done  — only when picking runs.
    ///   Checking needs packing done  — or picking, when packing does not run — or nothing at all
    ///                                  under Check-only.
    /// </summary>
    public static class WorkflowGate
    {
        /// <summary>
        /// Returns null when the order may be packed, or the message to show when it may not.
        /// </summary>
        public static string BlockPacking(SalesOrderResponse order)
        {
            if (order == null) return null;

            if (WorkflowConfig.UsePicking && !order.Picked)
                return "Picking incomplete";

            return null;
        }

        /// <summary>
        /// Returns null when the order may be checked, or the message to show when it may not.
        /// </summary>
        public static string BlockChecking(SalesOrderResponse order)
        {
            if (order == null) return null;

            if (WorkflowConfig.UsePacking && !order.Packed)
                return "Packing incomplete";

            if (!WorkflowConfig.UsePacking && WorkflowConfig.UsePicking && !order.Picked)
                return "Picking incomplete";

            return null;
        }
    }
}
