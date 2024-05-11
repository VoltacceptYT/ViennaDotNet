using static ViennaDotNet.ApiServer.Types.Journal.JournalRecord;

namespace ViennaDotNet.ApiServer.Types.Journal
{
    public record JournalRecord(
         Dictionary<string, InventoryJournalEntry> inventoryJournal,
         ActivityLogEntry[] activityLog
    )
    {
        public record InventoryJournalEntry(
            string firstSeen,
            string lastSeen,
            int amountCollected
        )
        {
        }

        public record ActivityLogEntry(
            // TODO
        )
        {
        }
    }
}
