using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Primitives;
using System.Diagnostics;
using System.Security.Claims;
using ViennaDotNet.ApiServer.Exceptions;
using ViennaDotNet.ApiServer.Utils;
using ViennaDotNet.Common;
using ViennaDotNet.Common.Excceptions;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB.Models.Player;
using ViennaDotNet.StaticData;
using BurnRate = ViennaDotNet.ApiServer.Types.Common.BurnRate;
using CraftingCalculator = ViennaDotNet.ApiServer.Utils.CraftingCalculator;
using CraftingSlot = ViennaDotNet.DB.Models.Player.Workshop.CraftingSlot;
using CraftingSlots = ViennaDotNet.DB.Models.Player.Workshop.CraftingSlots;
using EarthApiResponse = ViennaDotNet.ApiServer.Utils.EarthApiResponse;
using EarthDB = ViennaDotNet.DB.EarthDB;
using ExpectedPurchasePriceR = ViennaDotNet.ApiServer.Types.Common.ExpectedPurchasePriceR;
using FinishPrice = ViennaDotNet.ApiServer.Types.Workshop.FinishPrice;
using Hotbar = ViennaDotNet.DB.Models.Player.Hotbar;
using InputItem = ViennaDotNet.DB.Models.Player.Workshop.InputItem;
using Inventory = ViennaDotNet.DB.Models.Player.Inventory;
using Journal = ViennaDotNet.DB.Models.Player.Journal;
using NonStackableItemInstance = ViennaDotNet.DB.Models.Common.NonStackableItemInstance;
using OutputItem = ViennaDotNet.ApiServer.Types.Workshop.OutputItem;
using Profile = ViennaDotNet.DB.Models.Player.Profile;
using Rewards = ViennaDotNet.ApiServer.Utils.Rewards;
using SmeltingCalculator = ViennaDotNet.ApiServer.Utils.SmeltingCalculator;
using SmeltingSlot = ViennaDotNet.DB.Models.Player.Workshop.SmeltingSlot;
using SmeltingSlots = ViennaDotNet.DB.Models.Player.Workshop.SmeltingSlots;
using SplitRubies = ViennaDotNet.ApiServer.Types.Profile.SplitRubies;
using State = ViennaDotNet.ApiServer.Types.Workshop.State;
using TimeFormatter = ViennaDotNet.ApiServer.Utils.TimeFormatter;
using UnlockPrice = ViennaDotNet.ApiServer.Types.Workshop.UnlockPrice;

namespace ViennaDotNet.ApiServer.Controllers;

[Authorize]
[ApiVersion("1.1")]
[Route("1/api/v{version:apiVersion}")]
public class WorkshopRouter : ControllerBase
{
    private static EarthDB earthDB => Program.DB;
    private static StaticData.StaticData staticData => Program.staticData;

    [HttpGet("player/utilityBlocks")]
    public async Task<IActionResult> GetUtilityBlocks(CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId))
            return BadRequest();

        // request.timestamp
        long requestStartedOn = HttpContext.GetTimestamp();

        EarthDB.Results.GenericResult<CraftingSlots> craftingSlotsResult;
        EarthDB.Results.GenericResult<SmeltingSlots> smeltingSlotsResult;
        try
        {
            EarthDB.Results results = await new EarthDB.Query(false)
                .Get("crafting", playerId, typeof(CraftingSlots))
                .Get("smelting", playerId, typeof(SmeltingSlots))
                .ExecuteAsync(earthDB, cancellationToken);
            craftingSlotsResult = results.GetGeneric<CraftingSlots>("crafting");
            smeltingSlotsResult = results.GetGeneric<SmeltingSlots>("smelting");
        }
        catch (EarthDB.DatabaseException ex)
        {
            throw new ServerErrorException(ex);
        }

        Dictionary<string, object> workshop = new()
        {
            ["crafting"] = new Dictionary<string, object>()
            {
                ["1"] = CraftingSlotModelToResponseIncludingLocked(craftingSlotsResult.GValue.slots[0], requestStartedOn, craftingSlotsResult.version, 1),
                ["2"] = CraftingSlotModelToResponseIncludingLocked(craftingSlotsResult.GValue.slots[1], requestStartedOn, craftingSlotsResult.version, 2),
                ["3"] = CraftingSlotModelToResponseIncludingLocked(craftingSlotsResult.GValue.slots[2], requestStartedOn, craftingSlotsResult.version, 3),
            },
            ["smelting"] = new Dictionary<string, object>()
            {
                ["1"] = SmeltingSlotModelToResponseIncludingLocked(smeltingSlotsResult.GValue.slots[0], requestStartedOn, smeltingSlotsResult.version, 1),
                ["2"] = SmeltingSlotModelToResponseIncludingLocked(smeltingSlotsResult.GValue.slots[1], requestStartedOn, smeltingSlotsResult.version, 2),
                ["3"] = SmeltingSlotModelToResponseIncludingLocked(smeltingSlotsResult.GValue.slots[2], requestStartedOn, smeltingSlotsResult.version, 3),
            },
        };

        string resp = Json.Serialize(new EarthApiResponse(workshop));
        return Content(resp, "application/json");
    }

    [HttpGet("crafting/{slotIndex}")]
    public async Task<IActionResult> GetCraftingStatus(int slotIndex, CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId) || slotIndex < 1 || slotIndex > 3)
            return BadRequest();

        // request.timestamp
        long requestStartedOn = HttpContext.GetTimestamp();

        try
        {
            EarthDB.Results results = await new EarthDB.Query(false)
                .Get("crafting", playerId, typeof(CraftingSlots))
                .ExecuteAsync(earthDB, cancellationToken);
            EarthDB.Results.GenericResult<CraftingSlots> craftingSlotsResult = results.GetGeneric<CraftingSlots>("crafting");

            string resp = Json.Serialize(new EarthApiResponse(CraftingSlotModelToResponseIncludingLocked(craftingSlotsResult.GValue.slots[slotIndex - 1], requestStartedOn, craftingSlotsResult.version, slotIndex)));
            return Content(resp, "application/json");
        }
        catch (EarthDB.DatabaseException ex)
        {
            throw new ServerErrorException(ex);
        }
    }

    [HttpGet("smelting/{slotIndex}")]
    public async Task<IActionResult> GetSmeltingStatus(int slotIndex, CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId) || slotIndex < 1 || slotIndex > 3)
            return BadRequest();

        // request.timestamp
        long requestStartedOn = HttpContext.GetTimestamp();

        try
        {
            EarthDB.Results results = await new EarthDB.Query(false)
                .Get("smelting", playerId, typeof(SmeltingSlots))
                .ExecuteAsync(earthDB, cancellationToken);
            EarthDB.Results.GenericResult<SmeltingSlots> smeltingSlotsResult = results.GetGeneric<SmeltingSlots>("smelting");

            string resp = Json.Serialize(new EarthApiResponse(SmeltingSlotModelToResponseIncludingLocked(smeltingSlotsResult.GValue.slots[slotIndex - 1], requestStartedOn, smeltingSlotsResult.version, slotIndex)));
            return Content(resp, "application/json");
        }
        catch (EarthDB.DatabaseException ex)
        {
            throw new ServerErrorException(ex);
        }
    }

    [HttpPost("crafting/{slotIndex}/start")]
    public async Task<IActionResult> StartCrafting(int slotIndex, CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId) || slotIndex < 1 || slotIndex > 3)
        {
            return BadRequest();
        }

        // request.timestamp
        long requestStartedOn = HttpContext.GetTimestamp();

        StartRequestCrafting? startRequest = await Request.Body.AsJsonAsync<StartRequestCrafting>(cancellationToken);
        if (startRequest is null || startRequest.Multiplier < 1)
        {
            return BadRequest();
        }

        if (startRequest.Ingredients.Any(item => item is null || item.Quantity < 1 || (item.ItemInstanceIds is not null && item.ItemInstanceIds.Length > 0 && item.ItemInstanceIds.Length != item.Quantity)))
        {
            return BadRequest();
        }

        Catalog.RecipesCatalog.CraftingRecipe? recipe = staticData.catalog.recipesCatalog.getCraftingRecipe(startRequest.RecipeId);

        if (recipe is null)
        {
            return BadRequest();
        }

        if (recipe.returnItems.Length > 0)
        {
            throw new UnsupportedOperationException(); // TODO: implement returnItems
        }

        try
        {
            EarthDB.Results results = await new EarthDB.Query(true)
                .Get("crafting", playerId, typeof(CraftingSlots))
                .Get("inventory", playerId, typeof(Inventory))
                .Get("hotbar", playerId, typeof(Hotbar))
                .Then(results1 =>
                {
                    EarthDB.Query query = new EarthDB.Query(true);

                    CraftingSlots craftingSlots = (CraftingSlots)results1.Get("crafting").Value;
                    CraftingSlot craftingSlot = craftingSlots.slots[slotIndex - 1];
                    Inventory inventory = (Inventory)results1.Get("inventory").Value;
                    Hotbar hotbar = (Hotbar)results1.Get("hotbar").Value;

                    if (craftingSlot.locked || craftingSlot.activeJob is not null)
                    {
                        return query;
                    }

                    InputItem[] providedItems = new InputItem[startRequest.Ingredients.Length];
                    for (int index = 0; index < startRequest.Ingredients.Length; index++)
                    {
                        StartRequestCrafting.Item item = startRequest.Ingredients[index];
                        if (item.ItemInstanceIds is null || item.ItemInstanceIds.Length == 0)
                        {
                            if (!inventory.takeItems(item.ItemId, item.Quantity))
                            {
                                return query;
                            }

                            providedItems[index] = new InputItem(item.ItemId, item.Quantity, []);
                        }
                        else
                        {
                            NonStackableItemInstance[]? instances = inventory.takeItems(item.ItemId, item.ItemInstanceIds);
                            if (instances is null)
                            {
                                return query;
                            }

                            providedItems[index] = new InputItem(item.ItemId, item.Quantity, instances);
                        }
                    }

                    hotbar.limitToInventory(inventory);

                    LinkedList<LinkedList<InputItem>> inputItems = [];
                    foreach (Catalog.RecipesCatalog.CraftingRecipe.Ingredient ingredient in recipe.ingredients)
                    {
                        LinkedList<InputItem> ingredientItems = [];
                        int requiredCount = ingredient.count * startRequest.Multiplier;
                        for (int index = 0; index < providedItems.Length; index++)
                        {
                            InputItem providedItem = providedItems[index];
                            if (providedItem.count == 0)
                            {
                                continue;
                            }

                            if (!ingredient.possibleItemIds.Any(id => id == providedItem.id))
                            {
                                continue;
                            }

                            if (requiredCount > providedItem.count)
                            {
                                requiredCount -= providedItem.count;
                                ingredientItems.AddLast(providedItem);
                                providedItems[index] = new InputItem(providedItem.id, 0, []);
                            }
                            else
                            {
                                NonStackableItemInstance[] takenInstances;
                                NonStackableItemInstance[] remainingInstances;
                                if (providedItem.instances.Length > 0)
                                {
                                    takenInstances = ArrayExtensions.CopyOfRange(providedItem.instances, 0, requiredCount);
                                    remainingInstances = ArrayExtensions.CopyOfRange(providedItem.instances, requiredCount, providedItem.count);
                                }
                                else
                                {
                                    takenInstances = [];
                                    remainingInstances = [];
                                }

                                ingredientItems.AddLast(new InputItem(providedItem.id, requiredCount, takenInstances));
                                providedItems[index] = new InputItem(providedItem.id, providedItem.count - requiredCount, remainingInstances);
                                requiredCount = 0;
                            }

                            if (requiredCount == 0)
                            {
                                break;
                            }
                        }

                        if (requiredCount > 0)
                        {
                            return query;
                        }

                        if (ingredientItems.Count == 0)
                        {
                            throw new UnreachableException();
                        }

                        inputItems.AddLast(ingredientItems);
                    }

                    if (inputItems.Count != recipe.ingredients.Length)
                    {
                        throw new UnreachableException();
                    }

                    if (providedItems.Any(item => item.count > 0))
                    {
                        return query;
                    }

                    craftingSlot.activeJob = new CraftingSlot.ActiveJob(startRequest.SessionId, recipe.id, requestStartedOn, inputItems.Select(inputItems1 => inputItems1.ToArray()).ToArray(), startRequest.Multiplier, 0, false);

                    query.Update("crafting", playerId, craftingSlots).Update("inventory", playerId, inventory).Update("hotbar", playerId, hotbar);

                    return query;
                })
                .ExecuteAsync(earthDB, cancellationToken);

            string resp = Json.Serialize(new EarthApiResponse(new Dictionary<string, object>(), new EarthApiResponse.UpdatesResponse(results)));
            return Content(resp, "application/json");
        }
        catch (EarthDB.DatabaseException ex)
        {
            throw new ServerErrorException(ex);
        }
    }

    [HttpPost("smelting/{slotIndex}/start")]
    public async Task<IActionResult> StartSmelting(int slotIndex, CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId) || slotIndex < 1 || slotIndex > 3)
        {
            return BadRequest();
        }

        // request.timestamp
        long requestStartedOn = HttpContext.GetTimestamp();

        StartRequestSmelting? startRequest = await Request.Body.AsJsonAsync<StartRequestSmelting>(cancellationToken);
        if (startRequest is null || startRequest.Multiplier < 1)
        {
            return BadRequest();
        }

        if (startRequest.Input.Quantity < 1 || (startRequest.Input.ItemInstanceIds is not null && startRequest.Input.ItemInstanceIds.Length > 0 && startRequest.Input.ItemInstanceIds.Length != startRequest.Input.Quantity))
        {
            return BadRequest();
        }

        if (startRequest.Fuel is not null && startRequest.Fuel.Quantity > 0 && startRequest.Fuel.ItemInstanceIds is not null && startRequest.Fuel.ItemInstanceIds.Length > 0 && startRequest.Fuel.ItemInstanceIds.Length != startRequest.Fuel.Quantity)
        {
            return BadRequest();
        }

        Catalog.RecipesCatalog.SmeltingRecipe? recipe = staticData.catalog.recipesCatalog.getSmeltingRecipe(startRequest.RecipeId);
        Catalog.ItemsCatalog.Item? fuelCatalogItem = startRequest.Fuel is not null ? staticData.catalog.itemsCatalog.getItem(startRequest.Fuel.ItemId) : null;
        if (recipe is null)
        {
            return BadRequest();
        }

        if (startRequest.Fuel is not null && (fuelCatalogItem is null || fuelCatalogItem.fuelInfo is null))
        {
            return BadRequest();
        }

        if (recipe.returnItemId is not null)
        {
            throw new UnsupportedOperationException(); // TODO: implement returnItems
        }

        Debug.Assert(fuelCatalogItem is not null);
        Debug.Assert(fuelCatalogItem.fuelInfo is not null);

        if (startRequest.Fuel is not null && fuelCatalogItem.fuelInfo.returnItemId is not null)
        {
            throw new UnsupportedOperationException(); // TODO: implement returnItems
        }

        if (startRequest.Input.ItemId != recipe.input || startRequest.Input.Quantity != startRequest.Multiplier)
        {
            return BadRequest();
        }

        try
        {
            EarthDB.Results results = await new EarthDB.Query(true)
                .Get("smelting", playerId, typeof(SmeltingSlots))
                .Get("inventory", playerId, typeof(Inventory))
                .Get("hotbar", playerId, typeof(Hotbar))
                .Then(results1 =>
                {
                    EarthDB.Query query = new EarthDB.Query(true);

                    SmeltingSlots smeltingSlots = (SmeltingSlots)results1.Get("smelting").Value;
                    SmeltingSlot smeltingSlot = smeltingSlots.slots[slotIndex - 1];
                    Inventory inventory = (Inventory)results1.Get("inventory").Value;
                    Hotbar hotbar = (Hotbar)results1.Get("hotbar").Value;

                    if (smeltingSlot.locked || smeltingSlot.activeJob is not null)
                    {
                        return query;
                    }

                    InputItem input;
                    if (startRequest.Input.ItemInstanceIds is null || startRequest.Input.ItemInstanceIds.Length == 0)
                    {
                        if (!inventory.takeItems(startRequest.Input.ItemId, startRequest.Input.Quantity))
                            return query;

                        input = new InputItem(startRequest.Input.ItemId, startRequest.Input.Quantity, []);
                    }
                    else
                    {
                        NonStackableItemInstance[]? instances = inventory.takeItems(startRequest.Input.ItemId, startRequest.Input.ItemInstanceIds);
                        if (instances is null)
                            return query;

                        input = new InputItem(startRequest.Input.ItemId, startRequest.Input.Quantity, instances);
                    }

                    SmeltingSlot.Fuel? fuel;
                    int requiredFuelHeat = recipe.heatRequired * startRequest.Multiplier - (smeltingSlot.burning is not null ? smeltingSlot.burning.remainingHeat : 0);
                    if (startRequest.Fuel is not null && startRequest.Fuel.Quantity > 0)
                    {
                        int requiredFuelCount = 0;
                        while (requiredFuelHeat > 0)
                        {
                            requiredFuelCount += 1;
                            requiredFuelHeat -= fuelCatalogItem.fuelInfo.heatPerSecond * fuelCatalogItem.fuelInfo.burnTime;
                        }

                        if (startRequest.Fuel.Quantity < requiredFuelCount)
                        {
                            return query;
                        }

                        if (requiredFuelCount > 0)
                        {
                            InputItem fuelItem;
                            if (startRequest.Fuel.ItemInstanceIds is null || startRequest.Fuel.ItemInstanceIds.Length == 0)
                            {
                                if (!inventory.takeItems(startRequest.Fuel.ItemId, requiredFuelCount))
                                {
                                    return query;
                                }

                                fuelItem = new InputItem(startRequest.Fuel.ItemId, requiredFuelCount, []);
                            }
                            else
                            {
                                NonStackableItemInstance[]? instances = inventory.takeItems(startRequest.Fuel.ItemId, ArrayExtensions.CopyOfRange(startRequest.Fuel.ItemInstanceIds, 0, requiredFuelCount));
                                if (instances is null)
                                {
                                    return query;
                                }

                                fuelItem = new InputItem(startRequest.Fuel.ItemId, requiredFuelCount, instances);
                            }

                            fuel = new SmeltingSlot.Fuel(fuelItem, fuelCatalogItem.fuelInfo.burnTime, fuelCatalogItem.fuelInfo.heatPerSecond);
                        }
                        else
                        {
                            fuel = null;
                        }
                    }
                    else
                    {
                        if (requiredFuelHeat > 0)
                        {
                            return query;
                        }

                        fuel = null;
                    }

                    hotbar.limitToInventory(inventory);

                    smeltingSlot.activeJob = new SmeltingSlot.ActiveJob(startRequest.SessionId, recipe.id, requestStartedOn, input, fuel, startRequest.Multiplier, 0, false);

                    query.Update("smelting", playerId, smeltingSlots).Update("inventory", playerId, inventory).Update("hotbar", playerId, hotbar);

                    return query;
                })
                .ExecuteAsync(earthDB, cancellationToken);

            string resp = Json.Serialize(new EarthApiResponse(new Dictionary<string, object>(), new EarthApiResponse.UpdatesResponse(results)));
            return Content(resp, "application/json");
        }
        catch (EarthDB.DatabaseException ex)
        {
            throw new ServerErrorException(ex);
        }
    }

    [HttpPost("crafting/{slotIndex}/collectItems")]
    public async Task<IActionResult> CollectCraftingItems(int slotIndex, CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId) || slotIndex < 1 || slotIndex > 3)
            return BadRequest();

        // request.timestamp
        long requestStartedOn = HttpContext.GetTimestamp();

        try
        {
            EarthDB.Results results = await new EarthDB.Query(true)
                .Get("crafting", playerId, typeof(CraftingSlots))
                .Then(results1 =>
                {
                    CraftingSlots craftingSlots = (CraftingSlots)results1.Get("crafting").Value;
                    CraftingSlot craftingSlot = craftingSlots.slots[slotIndex - 1];

                    Rewards rewards = new Rewards();
                    if (craftingSlot.activeJob is not null)
                    {
                        CraftingCalculator.State state = CraftingCalculator.CalculateState(requestStartedOn, craftingSlot.activeJob, staticData.catalog);

                        int quantity = state.AvailableRounds * state.Output.Count;
                        if (quantity > 0)
                            rewards.addItem(state.Output.Id, quantity);

                        if (state.Completed)
                            craftingSlot.activeJob = null;
                        else
                        {
                            CraftingSlot.ActiveJob activeJob = craftingSlot.activeJob;
                            craftingSlot.activeJob = new CraftingSlot.ActiveJob(activeJob.sessionId, activeJob.recipeId, activeJob.startTime, activeJob.input, activeJob.totalRounds, activeJob.collectedRounds + state.AvailableRounds, activeJob.finishedEarly);
                        }
                    }

                    return new EarthDB.Query(true)
                        .Update("crafting", playerId, craftingSlots)
                        .Then(ActivityLogUtils.AddEntry(playerId, new ActivityLog.CraftingCompletedEntry(requestStartedOn, rewards.ToDBRewardsModel())))
                        .Then(rewards.toRedeemQuery(playerId, requestStartedOn, staticData));
                })
                .ExecuteAsync(earthDB, cancellationToken);

            string resp = Json.Serialize(new EarthApiResponse(new Dictionary<string, object>()
            {
                { "rewards", ((Rewards) results.getExtra("rewards")).ToApiResponse() }
            }, new EarthApiResponse.UpdatesResponse(results)));
            return Content(resp, "application/json");
        }
        catch (EarthDB.DatabaseException ex)
        {
            throw new ServerErrorException(ex);
        }
    }

    [HttpPost("smelting/{slotIndex}/collectItems")]
    public async Task<IActionResult> CollectSmeltingItems(int slotIndex, CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId) || slotIndex < 1 || slotIndex > 3)
            return BadRequest();

        // request.timestamp
        long requestStartedOn = HttpContext.GetTimestamp();

        try
        {
            EarthDB.Results results = await new EarthDB.Query(true)
                .Get("smelting", playerId, typeof(SmeltingSlots))
                .Then(results1 =>
                {
                    SmeltingSlots smeltingSlots = (SmeltingSlots)results1.Get("smelting").Value;
                    SmeltingSlot smeltingSlot = smeltingSlots.slots[slotIndex - 1];

                    Rewards rewards = new Rewards();
                    if (smeltingSlot.activeJob is not null)
                    {
                        SmeltingCalculator.State state = SmeltingCalculator.CalculateState(requestStartedOn, smeltingSlot.activeJob, smeltingSlot.burning, staticData.catalog);

                        int quantity = state.AvailableRounds * state.Output.Count;
                        if (quantity > 0)
                            rewards.addItem(state.Output.Id, quantity);

                        if (state.Completed)
                        {
                            smeltingSlot.activeJob = null;
                            if (state.RemainingHeat > 0)
                                smeltingSlot.burning = new SmeltingSlot.Burning(
                                    state.CurrentBurningFuel,
                                    state.RemainingHeat
                                );
                            else
                                smeltingSlot.burning = null;
                        }
                        else
                        {
                            SmeltingSlot.ActiveJob activeJob = smeltingSlot.activeJob;
                            smeltingSlot.activeJob = new SmeltingSlot.ActiveJob(activeJob.sessionId, activeJob.recipeId, activeJob.startTime, activeJob.input, activeJob.addedFuel, activeJob.totalRounds, activeJob.collectedRounds + state.AvailableRounds, activeJob.finishedEarly);
                        }
                    }

                    return new EarthDB.Query(true)
                        .Update("smelting", playerId, smeltingSlots)
                        .Then(ActivityLogUtils.AddEntry(playerId, new ActivityLog.SmeltingCompletedEntry(requestStartedOn, rewards.ToDBRewardsModel())))
                        .Then(rewards.toRedeemQuery(playerId, requestStartedOn, staticData));
                })
                .ExecuteAsync(earthDB, cancellationToken);

            string resp = Json.Serialize(new EarthApiResponse(new Dictionary<string, object>()
            {
                { "rewards", ((Rewards) results.getExtra("rewards")).ToApiResponse() }
            }, new EarthApiResponse.UpdatesResponse(results)));
            return Content(resp, "application/json");
        }
        catch (EarthDB.DatabaseException ex)
        {
            throw new ServerErrorException(ex);
        }
    }

    [HttpPost("crafting/{slotIndex}/stop")]
    public async Task<IActionResult> StopCraftingJob(int slotIndex, CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId) || slotIndex < 1 || slotIndex > 3)
            return BadRequest();

        // request.timestamp
        long requestStartedOn = HttpContext.GetTimestamp();

        try
        {
            EarthDB.Results results = await new EarthDB.Query(true)
                .Get("crafting", playerId, typeof(CraftingSlots))
                .Get("inventory", playerId, typeof(Inventory))
                .Get("journal", playerId, typeof(Journal))
                .Then(results1 =>
                {
                    EarthDB.Query query = new EarthDB.Query(true);
                    query.Get("crafting", playerId, typeof(CraftingSlots));

                    CraftingSlots craftingSlots = (CraftingSlots)results1.Get("crafting").Value;
                    CraftingSlot craftingSlot = craftingSlots.slots[slotIndex - 1];
                    Inventory inventory = (Inventory)results1.Get("inventory").Value;
                    Journal journal = (Journal)results1.Get("journal").Value;

                    if (craftingSlot.activeJob is null)
                        return query;

                    CraftingCalculator.State state = CraftingCalculator.CalculateState(requestStartedOn, craftingSlot.activeJob, staticData.catalog);

                    foreach (InputItem inputItem in state.Input)
                    {
                        if (inputItem.instances.Length > 0)
                            inventory.addItems(inputItem.id, [.. inputItem.instances.Select(instance => new NonStackableItemInstance(instance.instanceId, instance.wear))]);
                        else if (inputItem.count > 0)
                            inventory.addItems(inputItem.id, inputItem.count);

                        journal.addCollectedItem(inputItem.id, requestStartedOn, 0);
                    }

                    Rewards rewards = new Rewards();
                    int outputQuantity = state.AvailableRounds * state.Output.Count;
                    if (outputQuantity > 0)
                    {
                        rewards.addItem(state.Output.Id, outputQuantity);
                    }

                    craftingSlot.activeJob = null;

                    query.Update("crafting", playerId, craftingSlots).Update("inventory", playerId, inventory).Update("journal", playerId, journal);
                    query.Then(ActivityLogUtils.AddEntry(playerId, new ActivityLog.CraftingCompletedEntry(requestStartedOn, rewards.ToDBRewardsModel())), false);
                    query.Then(rewards.toRedeemQuery(playerId, requestStartedOn, staticData), false);

                    return query;
                })
                .ExecuteAsync(earthDB, cancellationToken);

            EarthDB.Results.GenericResult<CraftingSlots> craftingSlotsResult = results.GetGeneric<CraftingSlots>("crafting");

            string resp = Json.Serialize(new EarthApiResponse(CraftingSlotModelToResponse(craftingSlotsResult.GValue.slots[slotIndex - 1], requestStartedOn, craftingSlotsResult.version), new EarthApiResponse.UpdatesResponse(results)));
            return Content(resp, "application/json");
        }
        catch (EarthDB.DatabaseException ex)
        {
            throw new ServerErrorException(ex);
        }
    }

    [HttpPost("smelting/{slotIndex}/stop")]
    public async Task<IActionResult> StopSmeltingJob(int slotIndex, CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId) || slotIndex < 1 || slotIndex > 3)
            return BadRequest();

        // request.timestamp
        long requestStartedOn = HttpContext.GetTimestamp();

        try
        {
            EarthDB.Results results = await new EarthDB.Query(true)
                .Get("smelting", playerId, typeof(SmeltingSlots))
                .Get("inventory", playerId, typeof(Inventory))
                .Get("journal", playerId, typeof(Journal))
                .Then(results1 =>
                {
                    EarthDB.Query query = new EarthDB.Query(true);
                    query.Get("smelting", playerId, typeof(SmeltingSlots));

                    SmeltingSlots smeltingSlots = (SmeltingSlots)results1.Get("smelting").Value;
                    SmeltingSlot smeltingSlot = smeltingSlots.slots[slotIndex - 1];
                    Inventory inventory = (Inventory)results1.Get("inventory").Value;
                    Journal journal = (Journal)results1.Get("journal").Value;

                    if (smeltingSlot.activeJob is null)
                        return query;

                    SmeltingCalculator.State state = SmeltingCalculator.CalculateState(requestStartedOn, smeltingSlot.activeJob, smeltingSlot.burning, staticData.catalog);

                    if (state.Input.instances.Length > 0)
                        inventory.addItems(state.Input.id, [.. state.Input.instances.Select(instance => new NonStackableItemInstance(instance.instanceId, instance.wear))]);
                    else if (state.Input.count > 0)
                        inventory.addItems(state.Input.id, state.Input.count);

                    journal.addCollectedItem(state.Input.id, requestStartedOn, 0);

                    if (state.RemainingAddedFuel is not null)
                    {
                        if (state.RemainingAddedFuel.item.instances.Length > 0)
                            inventory.addItems(state.RemainingAddedFuel.item.id, [.. state.RemainingAddedFuel.item.instances.Select(instance => new NonStackableItemInstance(instance.instanceId, instance.wear))]);
                        else if (state.RemainingAddedFuel.item.count > 0)
                            inventory.addItems(state.RemainingAddedFuel.item.id, state.RemainingAddedFuel.item.count);

                        journal.addCollectedItem(state.RemainingAddedFuel.item.id, requestStartedOn, 0);
                    }

                    Rewards rewards = new Rewards();
                    int outputQuantity = state.AvailableRounds * state.Output.Count;
                    if (outputQuantity > 0)
                    {
                        rewards.addItem(state.Output.Id, outputQuantity);
                    }

                    smeltingSlot.activeJob = null;
                    if (state.RemainingHeat > 0)
                        smeltingSlot.burning = new SmeltingSlot.Burning(state.CurrentBurningFuel, state.RemainingHeat);
                    else
                        smeltingSlot.burning = null;

                    query.Update("smelting", playerId, smeltingSlots).Update("inventory", playerId, inventory).Update("journal", playerId, journal);
                    query.Then(ActivityLogUtils.AddEntry(playerId, new ActivityLog.SmeltingCompletedEntry(requestStartedOn, rewards.ToDBRewardsModel())), false);
                    query.Then(rewards.toRedeemQuery(playerId, requestStartedOn, staticData), false);

                    return query;
                })
                .ExecuteAsync(earthDB, cancellationToken);

            EarthDB.Results.GenericResult<SmeltingSlots> smeltingSlotsResult = results.GetGeneric<SmeltingSlots>("smelting");

            string resp = Json.Serialize(new EarthApiResponse(SmeltingSlotModelToResponse(smeltingSlotsResult.GValue.slots[slotIndex - 1], requestStartedOn, smeltingSlotsResult.version), new EarthApiResponse.UpdatesResponse(results)));
            return Content(resp, "application/json");
        }
        catch (EarthDB.DatabaseException ex)
        {
            throw new ServerErrorException(ex);
        }
    }

    [HttpPost("crafting/{slotIndex}/finish")]
    public async Task<IActionResult> FinishCrafting(int slotIndex, CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId) || slotIndex < 1 || slotIndex > 3)
            return BadRequest();

        // request.timestamp
        long requestStartedOn = HttpContext.GetTimestamp();

        ExpectedPurchasePriceR? expectedPurchasePrice = await Request.Body.AsJsonAsync<ExpectedPurchasePriceR>(cancellationToken);
        if (expectedPurchasePrice is null || expectedPurchasePrice.ExpectedPurchasePrice < 0)
            return BadRequest();

        try
        {
            EarthDB.Results results = await new EarthDB.Query(true)
                .Get("crafting", playerId, typeof(CraftingSlots))
                .Get("profile", playerId, typeof(Profile))
                .Then(results1 =>
                {
                    EarthDB.Query query = new EarthDB.Query(true);
                    query.Get("profile", playerId, typeof(Profile));

                    CraftingSlots craftingSlots = (CraftingSlots)results1.Get("crafting").Value;
                    CraftingSlot craftingSlot = craftingSlots.slots[slotIndex - 1];
                    Profile profile = (Profile)results1.Get("profile").Value;

                    if (craftingSlot.activeJob is null)
                        return query;

                    CraftingCalculator.State state = CraftingCalculator.CalculateState(requestStartedOn, craftingSlot.activeJob, staticData.catalog);
                    if (state.Completed)
                        return query;

                    int remainingTime = (int)(state.TotalCompletionTime - requestStartedOn);
                    if (remainingTime < 0)
                        return query;

                    CraftingCalculator.FinishPrice finishPrice = CraftingCalculator.CalculateFinishPrice(remainingTime);

                    if (expectedPurchasePrice.ExpectedPurchasePrice < finishPrice.Price)
                        return query;

                    if (!profile.rubies.spend(finishPrice.Price))
                        return query;

                    CraftingSlot.ActiveJob activeJob = craftingSlot.activeJob;
                    craftingSlot.activeJob = new CraftingSlot.ActiveJob(activeJob.sessionId, activeJob.recipeId, activeJob.startTime, activeJob.input, activeJob.totalRounds, activeJob.collectedRounds, true);
                    query.Update("crafting", playerId, craftingSlots).Update("profile", playerId, profile);

                    return query;
                })
                .ExecuteAsync(earthDB, cancellationToken);

            Profile profile = (Profile)results.Get("profile").Value;

            string resp = Json.Serialize(new EarthApiResponse(new SplitRubies(profile.rubies.purchased, profile.rubies.earned), new EarthApiResponse.UpdatesResponse(results)));
            return Content(resp, "application/json");
        }
        catch (EarthDB.DatabaseException ex)
        {
            throw new ServerErrorException(ex);
        }
    }

    [HttpPost("smelting/{slotIndex}/finish")]
    public async Task<IActionResult> FinishSmelting(int slotIndex, CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId) || slotIndex < 1 || slotIndex > 3)
            return BadRequest();

        // request.timestamp
        long requestStartedOn = HttpContext.GetTimestamp();

        ExpectedPurchasePriceR? expectedPurchasePrice = await Request.Body.AsJsonAsync<ExpectedPurchasePriceR>(cancellationToken);
        if (expectedPurchasePrice is null || expectedPurchasePrice.ExpectedPurchasePrice < 0)
            return BadRequest();

        try
        {
            EarthDB.Results results = await new EarthDB.Query(true)
                .Get("smelting", playerId, typeof(SmeltingSlots))
                .Get("profile", playerId, typeof(Profile))
                .Then(results1 =>
                {
                    EarthDB.Query query = new EarthDB.Query(true);
                    query.Get("profile", playerId, typeof(Profile));

                    SmeltingSlots smeltingSlots = (SmeltingSlots)results1.Get("smelting").Value;
                    SmeltingSlot smeltingSlot = smeltingSlots.slots[slotIndex - 1];
                    Profile profile = (Profile)results1.Get("profile").Value;

                    if (smeltingSlot.activeJob is null)
                        return query;

                    SmeltingCalculator.State state = SmeltingCalculator.CalculateState(requestStartedOn, smeltingSlot.activeJob, smeltingSlot.burning, staticData.catalog);
                    if (state.Completed)
                        return query;

                    int remainingTime = (int)(state.TotalCompletionTime - requestStartedOn);
                    if (remainingTime < 0)
                        return query;

                    SmeltingCalculator.FinishPrice finishPrice = SmeltingCalculator.CalculateFinishPrice(remainingTime);

                    if (expectedPurchasePrice.ExpectedPurchasePrice < finishPrice.Price)
                        return query;

                    if (!profile.rubies.spend(finishPrice.Price))
                        return query;

                    SmeltingSlot.ActiveJob activeJob = smeltingSlot.activeJob;
                    smeltingSlot.activeJob = new SmeltingSlot.ActiveJob(activeJob.sessionId, activeJob.recipeId, activeJob.startTime, activeJob.input, activeJob.addedFuel, activeJob.totalRounds, activeJob.collectedRounds, true);

                    query.Update("smelting", playerId, smeltingSlots).Update("profile", playerId, profile);

                    return query;
                })
                .ExecuteAsync(earthDB, cancellationToken);

            Profile profile = (Profile)results.Get("profile").Value;

            string resp = Json.Serialize(new EarthApiResponse(new SplitRubies(profile.rubies.purchased, profile.rubies.earned), new EarthApiResponse.UpdatesResponse(results)));
            return Content(resp, "application/json");
        }
        catch (EarthDB.DatabaseException ex)
        {
            throw new ServerErrorException(ex);
        }
    }

    [HttpGet("crafting/finish/price")]
    public IActionResult GetCraftingPrice()
    {
        //TimeSpan remainingTime = TimeSpan.Parse(Request.Query["remainingTime"]);

        if (!Request.Query.TryGetValue("remainingTime", out StringValues _remainingTime))
            return BadRequest();

        int remainingTime;
        try
        {
            remainingTime = (int)TimeFormatter.ParseDuration(_remainingTime.ToString());
            if (remainingTime < 0)
                return BadRequest();
        }
        catch
        {
            return BadRequest();
        }

        CraftingCalculator.FinishPrice finishPrice = CraftingCalculator.CalculateFinishPrice(remainingTime);

        string resp = Json.Serialize(new EarthApiResponse(new FinishPrice(finishPrice.Price, 0, TimeFormatter.FormatDuration(finishPrice.ValidFor))));
        return Content(resp, "application/json");
    }

    [HttpGet("smelting/finish/price")]
    public IActionResult GetSmeltingPrice()
    {
        //TimeSpan remainingTime = TimeSpan.Parse(Request.Query["remainingTime"]);

        if (!Request.Query.TryGetValue("remainingTime", out StringValues _remainingTime))
            return BadRequest();

        int remainingTime;
        try
        {
            remainingTime = (int)TimeFormatter.ParseDuration(_remainingTime.ToString());
            if (remainingTime < 0)
                return BadRequest();
        }
        catch
        {
            return BadRequest();
        }

        SmeltingCalculator.FinishPrice finishPrice = SmeltingCalculator.CalculateFinishPrice(remainingTime);

        string resp = Json.Serialize(new EarthApiResponse(new FinishPrice(finishPrice.Price, 0, TimeFormatter.FormatDuration(finishPrice.ValidFor))));
        return Content(resp, "application/json");
    }

    [HttpPost("crafting/{slotIndex}/unlock")]
    public async Task<IActionResult> UnlockCraftingSlot(int slotIndex, CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId) || slotIndex < 1 || slotIndex > 3)
            return BadRequest();

        ExpectedPurchasePriceR? expectedPurchasePrice = await Request.Body.AsJsonAsync<ExpectedPurchasePriceR>(cancellationToken);
        if (expectedPurchasePrice is null || expectedPurchasePrice.ExpectedPurchasePrice < 0)
            return BadRequest();

        try
        {
            EarthDB.Results results = await new EarthDB.Query(true)
                .Get("crafting", playerId, typeof(CraftingSlots))
                .Get("profile", playerId, typeof(Profile))
                .Then(results1 =>
                {
                    EarthDB.Query query = new EarthDB.Query(true);

                    CraftingSlots craftingSlots = (CraftingSlots)results1.Get("crafting").Value;
                    CraftingSlot craftingSlot = craftingSlots.slots[slotIndex - 1];
                    Profile profile = (Profile)results1.Get("profile").Value;

                    if (!craftingSlot.locked)
                        return query;

                    int unlockPrice = CraftingCalculator.calculateUnlockPrice(slotIndex);

                    if (expectedPurchasePrice.ExpectedPurchasePrice != unlockPrice)
                        return query;

                    if (!profile.rubies.spend(unlockPrice))
                        return query;

                    craftingSlot.locked = false;

                    query.Update("crafting", playerId, craftingSlots).Update("profile", playerId, profile);

                    return query;
                })
                .ExecuteAsync(earthDB, cancellationToken);

            string resp = Json.Serialize(new EarthApiResponse(new Dictionary<string, object>(), new EarthApiResponse.UpdatesResponse(results)));
            return Content(resp, "application/json");
        }
        catch (EarthDB.DatabaseException ex)
        {
            throw new ServerErrorException(ex);
        }
    }

    [HttpPost("smelting/{slotIndex}/unlock")]
    public async Task<IActionResult> UnlockSmeltingSlot(int slotIndex, CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId) || slotIndex < 1 || slotIndex > 3)
            return BadRequest();

        ExpectedPurchasePriceR? expectedPurchasePrice = await Request.Body.AsJsonAsync<ExpectedPurchasePriceR>(cancellationToken);
        if (expectedPurchasePrice is null || expectedPurchasePrice.ExpectedPurchasePrice < 0)
            return BadRequest();

        try
        {
            EarthDB.Results results = await new EarthDB.Query(true)
                .Get("smelting", playerId, typeof(SmeltingSlots))
                .Get("profile", playerId, typeof(Profile))
                .Then(results1 =>
                {
                    EarthDB.Query query = new EarthDB.Query(true);

                    SmeltingSlots smeltingSlots = (SmeltingSlots)results1.Get("smelting").Value;
                    SmeltingSlot smeltingSlot = smeltingSlots.slots[slotIndex - 1];
                    Profile profile = (Profile)results1.Get("profile").Value;

                    if (!smeltingSlot.locked)
                        return query;

                    int unlockPrice = SmeltingCalculator.CalculateUnlockPrice(slotIndex);

                    if (expectedPurchasePrice.ExpectedPurchasePrice != unlockPrice)
                        return query;

                    if (!profile.rubies.spend(unlockPrice))
                        return query;

                    smeltingSlot.locked = false;

                    query.Update("smelting", playerId, smeltingSlots).Update("profile", playerId, profile);

                    return query;
                })
                .ExecuteAsync(earthDB, cancellationToken);

            string resp = Json.Serialize(new EarthApiResponse(new Dictionary<string, object>(), new EarthApiResponse.UpdatesResponse(results)));
            return Content(resp, "application/json");
        }
        catch (EarthDB.DatabaseException ex)
        {
            throw new ServerErrorException(ex);
        }
    }

    private Types.Workshop.CraftingSlot CraftingSlotModelToResponseIncludingLocked(CraftingSlot craftingSlotModel, long currentTime, int streamVersion, int slotIndex)
    {
        if (craftingSlotModel.locked)
            return new Types.Workshop.CraftingSlot(null, null, null, null, 0, 0, 0, null, null, State.LOCKED, null, new UnlockPrice(CraftingCalculator.calculateUnlockPrice(slotIndex), 0), streamVersion);
        else
            return CraftingSlotModelToResponse(craftingSlotModel, currentTime, streamVersion);
    }

    private static Types.Workshop.CraftingSlot CraftingSlotModelToResponse(CraftingSlot craftingSlotModel, long currentTime, int streamVersion)
    {
        if (craftingSlotModel.locked)
            throw new ArgumentException(nameof(craftingSlotModel));

        CraftingSlot.ActiveJob? activeJob = craftingSlotModel.activeJob;
        if (activeJob is not null)
        {
            CraftingCalculator.State state = CraftingCalculator.CalculateState(currentTime, activeJob, staticData.catalog);
            return new Types.Workshop.CraftingSlot(
                activeJob.sessionId,
                activeJob.recipeId,
                new OutputItem(state.Output.Id, state.Output.Count),
                [.. activeJob.input.SelectMany(inputItems => inputItems).Select(item => new Types.Workshop.InputItem(
                    item.id,
                    item.count,
                    [.. item.instances.Select(item => item.instanceId)]
                ))],
                state.CompletedRounds,
                state.AvailableRounds,
                state.TotalRounds,
                !state.Completed ? TimeFormatter.FormatTime(state.NextCompletionTime) : null,
                !state.Completed ? TimeFormatter.FormatTime(state.TotalCompletionTime) : null,
                state.Completed ? State.COMPLETED : State.ACTIVE,
                null,
                null,
                streamVersion
            );
        }
        else
            return new Types.Workshop.CraftingSlot(null, null, null, null, 0, 0, 0, null, null, State.EMPTY, null, null, streamVersion);
    }

    private static Types.Workshop.SmeltingSlot SmeltingSlotModelToResponseIncludingLocked(SmeltingSlot smeltingSlotModel, long currentTime, int streamVersion, int slotIndex)
    {
        if (smeltingSlotModel.locked)
            return new Types.Workshop.SmeltingSlot(null, null, null, null, null, null, 0, 0, 0, null, null, State.LOCKED, null, new UnlockPrice(SmeltingCalculator.CalculateUnlockPrice(slotIndex), 0), streamVersion);
        else
            return SmeltingSlotModelToResponse(smeltingSlotModel, currentTime, streamVersion);
    }

    private static Types.Workshop.SmeltingSlot SmeltingSlotModelToResponse(SmeltingSlot smeltingSlotModel, long currentTime, int streamVersion)
    {
        if (smeltingSlotModel.locked)
            throw new ArgumentException(nameof(smeltingSlotModel));

        SmeltingSlot.ActiveJob? activeJob = smeltingSlotModel.activeJob;
        if (activeJob is not null)
        {
            SmeltingCalculator.State state = SmeltingCalculator.CalculateState(currentTime, activeJob, smeltingSlotModel.burning, staticData.catalog);

            Types.Workshop.SmeltingSlot.FuelR? fuel;
            if (state.RemainingAddedFuel is not null && state.RemainingAddedFuel.item.count > 0)
            {
                fuel = new Types.Workshop.SmeltingSlot.FuelR(
                    new BurnRate(state.RemainingAddedFuel.burnDuration, state.RemainingAddedFuel.heatPerSecond),
                    state.RemainingAddedFuel.item.id,
                    state.RemainingAddedFuel.item.count,
                    [.. state.RemainingAddedFuel.item.instances.Select(item => item.instanceId)]
                );
            }
            else
                fuel = null;

            Types.Workshop.SmeltingSlot.BurningR burning = new Types.Workshop.SmeltingSlot.BurningR(
                !state.Completed ? TimeFormatter.FormatTime(state.BurnStartTime) : null,
                !state.Completed ? TimeFormatter.FormatTime(state.BurnEndTime) : null,
                TimeFormatter.FormatDuration(state.RemainingHeat * 1000 / state.CurrentBurningFuel.heatPerSecond),
                (float)state.CurrentBurningFuel.burnDuration * state.CurrentBurningFuel.heatPerSecond - state.RemainingHeat,
                new Types.Workshop.SmeltingSlot.FuelR(
                    new BurnRate(state.CurrentBurningFuel.burnDuration, state.CurrentBurningFuel.heatPerSecond),
                    state.CurrentBurningFuel.item.id,
                    state.CurrentBurningFuel.item.count,
                    [.. state.CurrentBurningFuel.item.instances.Select(item => item.instanceId)]
                )
            );

            return new Types.Workshop.SmeltingSlot(
                fuel,
                burning,
                activeJob.sessionId,
                activeJob.recipeId,
                new OutputItem(state.Output.Id, state.Output.Count),
                state.Input.count > 0 ? [new Types.Workshop.InputItem(state.Input.id, state.Input.count, state.Input.instances.Select(item => item.instanceId).ToArray())] : [],
                state.CompletedRounds,
                state.AvailableRounds,
                state.TotalRounds,
                !state.Completed ? TimeFormatter.FormatTime(state.NextCompletionTime) : null,
                !state.Completed ? TimeFormatter.FormatTime(state.TotalCompletionTime) : null,
                state.Completed ? State.COMPLETED : State.ACTIVE,
                null,
                null,
                streamVersion
            );
        }
        else
        {
            SmeltingSlot.Burning? burningModel = smeltingSlotModel.burning;
            Types.Workshop.SmeltingSlot.BurningR? burning = burningModel is not null ? new Types.Workshop.SmeltingSlot.BurningR(
                null,
                null,
                TimeFormatter.FormatDuration(burningModel.remainingHeat * 1000 / burningModel.fuel.heatPerSecond),
                (float)burningModel.fuel.burnDuration * burningModel.fuel.heatPerSecond * burningModel.fuel.item.count - burningModel.remainingHeat,
                new Types.Workshop.SmeltingSlot.FuelR(
                    new BurnRate(burningModel.fuel.burnDuration, burningModel.fuel.heatPerSecond),
                    burningModel.fuel.item.id,
                    burningModel.fuel.item.count,
                    [.. burningModel.fuel.item.instances.Select(item => item.instanceId)]
                )
            ) : null;
            return new Types.Workshop.SmeltingSlot(null, burning, null, null, null, null, 0, 0, 0, null, null, State.EMPTY, null, null, streamVersion);
        }
    }

    private sealed record StartRequestCrafting(
        string SessionId,
        string RecipeId,
        int Multiplier,
        StartRequestCrafting.Item[] Ingredients
    )
    {
        public sealed record Item(
            string ItemId,
            int Quantity,
            string[] ItemInstanceIds
        );
    }

    private sealed record StartRequestSmelting(
        string SessionId,
        string RecipeId,
        int Multiplier,
        StartRequestSmelting.Item Input,
        StartRequestSmelting.Item Fuel
    )
    {
        public sealed record Item(
            string ItemId,
            int Quantity,
            string[] ItemInstanceIds
        );
    }
}
