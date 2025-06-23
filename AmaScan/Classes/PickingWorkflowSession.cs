using AmaScan.sqliteModels;
using Data.Model;

namespace AmaScan.Classes
{
    public class PickingWorkflowSession
    {
        public static SoHeader? CurrentSoHeader { get; set; }
        public static SoLine? CurrentSoLine { get; set; }

        public static void Clear()
        {
            CurrentSoHeader = null;
            CurrentSoLine = null;
        }
    }
}