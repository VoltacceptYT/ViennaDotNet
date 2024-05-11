using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Security.Claims;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB.Models.Player;
using ViennaDotNet.DB;
using ViennaDotNet.ApiServer.Exceptions;
using Uma.Uuid;
using ViennaDotNet.ApiServer.Utils;
using Newtonsoft.Json;

namespace ViennaDotNet.ApiServer.Controllers
{
    [Authorize]
    [ApiVersion("1.1")]
    [Route("1/api/v{version:apiVersion}/player/journal")]
    public class JournalController : ControllerBase
    {
        private static EarthDB earthDB => Program.DB;

        [HttpGet]
        public IActionResult Get()
        {
            string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(playerId))
                return BadRequest();

            Journal journalModel;
            try
            {
                EarthDB.Results results = new EarthDB.Query(false)
                    .Get("journal", playerId, typeof(Journal))
                    .Execute(earthDB);
                journalModel = (Journal)results.Get("journal").Value;
            }
            catch (EarthDB.DatabaseException exception)
            {
                throw new ServerErrorException(exception);
            }

            Dictionary<string, Types.Journal.JournalRecord.InventoryJournalEntry> inventoryJournal = new();
            journalModel.getItems().ForEach((uuid, itemJournalEntry) => inventoryJournal[uuid] = new Types.Journal.JournalRecord.InventoryJournalEntry(
                TimeFormatter.FormatTime(itemJournalEntry.firstSeen),
                TimeFormatter.FormatTime(itemJournalEntry.lastSeen),
                itemJournalEntry.amountCollected
            ));

            // TODO
            Types.Journal.JournalRecord.ActivityLogEntry[] activityLog = Array.Empty<Types.Journal.JournalRecord.ActivityLogEntry>();

            string resp = JsonConvert.SerializeObject(new EarthApiResponse(new Types.Journal.JournalRecord(inventoryJournal, activityLog)));
            return Content(resp, "application/json");
        }
    }
}
