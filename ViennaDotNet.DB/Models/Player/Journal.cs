using Newtonsoft.Json;
using Uma.Uuid;
using ViennaDotNet.Common.Utils;

namespace ViennaDotNet.DB.Models.Player
{
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class Journal
    {
        [JsonProperty]
        private Dictionary<string, ItemJournalEntry> items;

        public Journal()
        {
            items = new();
        }

        public Journal copy()
        {
            Journal journal = new Journal();
            journal.items.AddRange(items);
            return journal;
        }

        public Dictionary<string, ItemJournalEntry> getItems()
        {
            return new(items);
        }

        public ItemJournalEntry? getItem(string uuid)
        {
            return items.GetOrDefault(uuid, null);
        }

        public void touchItem(string uuid, long timestamp)
        {
            ItemJournalEntry? itemJournalEntry = items.GetOrDefault(uuid, null);

            if (itemJournalEntry == null)
                items[uuid] = new ItemJournalEntry(timestamp, timestamp, 0);
            else
                items[uuid] = new ItemJournalEntry(itemJournalEntry.firstSeen, timestamp, itemJournalEntry.amountCollected);
        }

        // TODO: find out what is supposed to count as a "collected item" - currently we count items from tappables *and* other rewards (e.g. challenge/level rewards, this also currently includes workshop output), but not from buildplates because that would be really difficult to track
        public void addCollectedItem(string uuid, int count)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            ItemJournalEntry? itemJournalEntry = items.GetOrDefault(uuid, null);
            if (itemJournalEntry == null)
                throw new InvalidOperationException("Item does not exist in journal, make sure to touch it or otherwise verify that it exists before calling addCollectedItem");

            items[uuid] = new ItemJournalEntry(itemJournalEntry.firstSeen, itemJournalEntry.lastSeen, itemJournalEntry.amountCollected + count);
        }

        public record ItemJournalEntry(
            long firstSeen,
            long lastSeen,
            int amountCollected
        )
        {
        }
    }
}
