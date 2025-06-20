using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Security.Claims;
using ViennaDotNet.ApiServer.Exceptions;
using ViennaDotNet.ApiServer.Types.Inventory;
using ViennaDotNet.ApiServer.Utils;
using ViennaDotNet.Common;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB;
using ViennaDotNet.DB.Models.Common;
using ViennaDotNet.DB.Models.Player;
using ViennaDotNet.StaticData;

namespace ViennaDotNet.ApiServer.Controllers;

[Authorize]
[ApiVersion("1.1")]
[Route("1/api/v{version:apiVersion}/inventory/survival")]
public class InventoryController : ControllerBase
{
    private static EarthDB earthDB => Program.DB;
    private static Catalog catalog => Program.staticData.catalog;

    [HttpGet]
    public async Task<IActionResult> GetInventory(CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId))
        {
            return BadRequest();
        }

        DB.Models.Player.Inventory inventoryModel;
        Hotbar hotbarModel;
        Journal journalModel;
        try
        {
            EarthDB.Results results = await new EarthDB.Query(false)
                .Get("inventory", playerId, typeof(DB.Models.Player.Inventory))
                .Get("hotbar", playerId, typeof(Hotbar))
                .Get("journal", playerId, typeof(Journal))
                .ExecuteAsync(earthDB, cancellationToken);

            inventoryModel = (DB.Models.Player.Inventory)results.Get("inventory").Value;
            hotbarModel = (Hotbar)results.Get("hotbar").Value;
            journalModel = (Journal)results.Get("journal").Value;
        }
        catch (EarthDB.DatabaseException ex)
        {
            throw new ServerErrorException(ex);
        }

        Dictionary<string, int?> hotbarItemCounts = [];
        foreach (var item in hotbarModel.items)
        {
            if (item is not null)
                hotbarItemCounts[item.uuid] = hotbarItemCounts.GetOrDefault(item.uuid, 0) + item.count;
        }

        HashSet<string> hotbarItemInstances = [];
        foreach (var item in hotbarModel.items)
        {
            if (item is not null && item.instanceId is not null)
                hotbarItemInstances.Add(item.instanceId);
        }

        Types.Inventory.Inventory inventory = new Types.Inventory.Inventory(
            [.. hotbarModel.items.Select(item => item is not null ? new HotbarItem(
                item.uuid,
                item.count,
                item.instanceId,
                item.instanceId is not null ? ItemWear.WearToHealth(item.uuid, inventoryModel.getItemInstance(item.uuid, item.instanceId)?.wear ?? 0, catalog.itemsCatalog) : 0.0f
                    ) : null)],
            [.. inventoryModel.getStackableItems().Select(item =>
            {
                string uuid = item.id;
                int count = item.count - hotbarItemCounts.GetOrDefault(uuid, 0) ?? 0;
                Journal.ItemJournalEntry itemJournalEntry = journalModel.getItem(uuid)!;
                string firstSeen = TimeFormatter.FormatTime(itemJournalEntry.firstSeen);
                string lastSeen = TimeFormatter.FormatTime(itemJournalEntry.lastSeen);

                return new StackableInventoryItem(
                    uuid,
                    count,
                    1,
                    new StackableInventoryItem.OnR(firstSeen),
                    new StackableInventoryItem.OnR(lastSeen)
                );
            })],
            [.. inventoryModel.getNonStackableItems().Select(item =>
            {
                string uuid = item.id;
                Journal.ItemJournalEntry itemJournalEntry = journalModel.getItem(uuid)!;
                string firstSeen = TimeFormatter.FormatTime(itemJournalEntry.firstSeen);
                string lastSeen = TimeFormatter.FormatTime(itemJournalEntry.lastSeen);
                return new NonStackableInventoryItem(
                    uuid,
                    [.. item.instances.Where(instance => !hotbarItemInstances.Contains(instance.instanceId)).Select(instance => new NonStackableInventoryItem.Instance(instance.instanceId, ItemWear.WearToHealth(item.id, instance.wear, catalog.itemsCatalog)))],
                    1,
                    new NonStackableInventoryItem.OnR(firstSeen),
                    new NonStackableInventoryItem.OnR(lastSeen)
                );
            })]
        );

        string resp = Json.Serialize(new EarthApiResponse(inventory));
        return Content(resp, "application/json");
    }

    [HttpGet("hotbar")]
    public async Task<IActionResult> GetHotbar(CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId))
        {
            return BadRequest();
        }

        SetHotbarRequestItem[]? setHotbarRequestItems = await Request.Body.AsJsonAsync<SetHotbarRequestItem[]>(cancellationToken);
        if (setHotbarRequestItems is null || setHotbarRequestItems.Length != 7)
        {
            return BadRequest();
        }

        DB.Models.Player.Inventory inventoryModel;
        Hotbar hotbarModel;
        try
        {
            EarthDB.Results results = await new EarthDB.Query(true)
                .Get("inventory", playerId, typeof(DB.Models.Player.Inventory))
                .Then(results1 =>
                {
                    Hotbar hotbar = new Hotbar();
                    for (int index = 0; index < hotbar.items.Length; index++)
                    {
                        SetHotbarRequestItem item = setHotbarRequestItems[index];
                        hotbar.items[index] = item is not null ? new Hotbar.Item(item.Id, item.Count, item.InstanceId) : null;
                    }

                    hotbar.limitToInventory((DB.Models.Player.Inventory)results1.Get("inventory").Value);
                    return new EarthDB.Query(true)
                        .Update("hotbar", playerId, hotbar)
                        .Get("inventory", playerId, typeof(DB.Models.Player.Inventory))
                        .Get("hotbar", playerId, typeof(Hotbar));
                })
                .ExecuteAsync(earthDB, cancellationToken);

            inventoryModel = (DB.Models.Player.Inventory)results.Get("inventory").Value;
            hotbarModel = (Hotbar)results.Get("hotbar").Value;
        }
        catch (EarthDB.DatabaseException ex)
        {
            throw new ServerErrorException(ex);
        }

        HotbarItem?[] hotbarItems = [.. hotbarModel.items.Select(item => item is not null ? new HotbarItem(
            item.uuid,
            item.count,
            item.instanceId,
            item.instanceId is not null ? ItemWear.WearToHealth(item.uuid, inventoryModel.getItemInstance(item.uuid, item.instanceId)!.wear, catalog.itemsCatalog) : 0.0f
        ) : null)];

        string resp = Json.Serialize(hotbarItems);
        return Content(resp, "application/json");
    }

    [HttpPost("{itemId}/consume")]
    public async Task<IActionResult> ConsumeItem(string itemId, CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId))
        {
            return BadRequest();
        }

        // request.timestamp
        long requestStartedOn = HttpContext.GetTimestamp();

        Catalog.ItemsCatalog.Item? item = catalog.itemsCatalog.getItem(itemId);

        if (item is null || item.consumeInfo is null)
        {
            return BadRequest();
        }

        try
        {
            EarthDB.Results results = await new EarthDB.Query(true)
                .Get("inventory", playerId, typeof(DB.Models.Player.Inventory))
                .Get("journal", playerId, typeof(Journal))
                .Get("profile", playerId, typeof(Profile))
                .Get("boosts", playerId, typeof(Boosts))
                .Then(results1 =>
                {
                    DB.Models.Player.Inventory inventory = (DB.Models.Player.Inventory)results1.Get("inventory").Value;
                    Journal journal = (Journal)results1.Get("journal").Value;
                    Profile profile = (Profile)results1.Get("profile").Value;
                    Boosts boosts = (Boosts)results1.Get("boosts").Value;

                    EarthDB.Query query = new EarthDB.Query(true);

                    if (!inventory.takeItems(itemId, 1))
                    {
                        return new EarthDB.Query(false);
                    }

                    string? returnItemId = item.consumeInfo.returnItemId;
                    if (returnItemId is not null)
                    {
                        Catalog.ItemsCatalog.Item? returnItem = catalog.itemsCatalog.getItem(returnItemId);
                        Debug.Assert(returnItem is not null);

                        if (returnItem.stackable)
                        {
                            inventory.addItems(returnItemId, 1);
                        }
                        else
                        {
                            inventory.addItems(returnItemId, [new NonStackableItemInstance(U.RandomUuid().ToString(), 0)]);
                        }

                        if (journal.addCollectedItem(returnItemId, requestStartedOn, 1) == 0)
                        {
                            if (returnItem.journalEntry is not null)
                            {
                                query.Then(TokenUtils.AddToken(playerId, new Tokens.JournalItemUnlockedToken(returnItemId)));
                            }
                        }
                    }

                    int healing = item.consumeInfo.heal;

                    int healingMultiplier = BoostUtils.GetActiveStatModifiers(boosts, requestStartedOn, catalog.itemsCatalog).FoodMultiplier;
                    if (healingMultiplier > 0)
                    {
                        healing = (healing * (healingMultiplier + 100)) / 100;
                    }

                    int maxPlayerHealth = BoostUtils.GetMaxPlayerHealth(boosts, requestStartedOn, catalog.itemsCatalog);
                    profile.health += healing;
                    if (profile.health > maxPlayerHealth)
                    {
                        profile.health = maxPlayerHealth;
                    }

                    query.Update("inventory", playerId, inventory).Update("journal", playerId, journal).Update("profile", playerId, profile);

                    return query;
                })
                .ExecuteAsync(earthDB, cancellationToken);

            string resp = Json.Serialize(new EarthApiResponse(null, new EarthApiResponse.UpdatesResponse(results)));
            return Content(resp, "application/json");
        }
        catch (EarthDB.DatabaseException exception)
        {
            throw new ServerErrorException(exception);
        }
    }

    private sealed record SetHotbarRequestItem(
        string Id,
        int Count,
        string? InstanceId
    );
}
