using Serilog;
using System.Diagnostics;
using System.Text;
using ViennaDotNet.Buildplate.Connector.Model;
using ViennaDotNet.Common;
using ViennaDotNet.Common.Buildplate.Connector.Model;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB;
using ViennaDotNet.DB.Models.Common;
using ViennaDotNet.DB.Models.Global;
using ViennaDotNet.DB.Models.Player;
using ViennaDotNet.EventBus.Client;
using ViennaDotNet.ObjectStore.Client;
using ViennaDotNet.StaticData;
using CICIBIEType = ViennaDotNet.StaticData.Catalog.ItemsCatalog.Item.BoostInfo.Effect.Type;

namespace ViennaDotNet.ApiServer.Utils;

public sealed class BuildplateInstanceRequestHandler
{
    public static void Start(EarthDB earthDB, EventBusClient eventBusClient, ObjectStoreClient objectStoreClient, Catalog catalog)
        => _ = new BuildplateInstanceRequestHandler(earthDB, eventBusClient, objectStoreClient, catalog);

    private readonly EarthDB _earthDB;
    private readonly ObjectStoreClient _objectStoreClient;
    private readonly Catalog _catalog;
    private static BuildplateInstancesManager _buildplateInstancesManager => Program.buildplateInstancesManager;

    public BuildplateInstanceRequestHandler(EarthDB earthDB, EventBusClient eventBusClient, ObjectStoreClient objectStoreClient, Catalog catalog)
    {
        _earthDB = earthDB;
        _objectStoreClient = objectStoreClient;
        _catalog = catalog;

        RequestHandler requestHandler = eventBusClient.addRequestHandler("buildplates", new RequestHandler.Handler(
            async request =>
            {
                try
                {
                    switch (request.type)
                    {
                        case "load":
                            {
                                BuildplateLoadRequest? buildplateLoadRequest = ReadRawRequest<BuildplateLoadRequest>(request.data);
                                if (buildplateLoadRequest is null)
                                    return null;

                                BuildplateLoadResponse? buildplateLoadResponse = await HandleLoad(buildplateLoadRequest.PlayerId, buildplateLoadRequest.BuildplateId);
                                return buildplateLoadResponse is not null ? Json.Serialize(buildplateLoadResponse) : null;
                            }
                        case "loadShared":
                            {
                                SharedBuildplateLoadRequest? sharedBuildplateLoadRequest = ReadRawRequest<SharedBuildplateLoadRequest>(request.data);
                                if (sharedBuildplateLoadRequest is null)
                                {
                                    return null;
                                }

                                BuildplateLoadResponse? buildplateLoadResponse = await HandleLoadShared(sharedBuildplateLoadRequest.SharedBuildplateId);
                                return buildplateLoadResponse is not null ? Json.Serialize(buildplateLoadResponse) : null;
                            }
                        case "loadEncounter":

                            {
                                EncounterBuildplateLoadRequest? encounterBuildplateLoadRequest = ReadRawRequest<EncounterBuildplateLoadRequest>(request.data);
                                if (encounterBuildplateLoadRequest is null)
                                {
                                    return null;
                                }

                                BuildplateLoadResponse? buildplateLoadResponse = await HandleLoadEncounter(encounterBuildplateLoadRequest.EncounterBuildplateId);
                                return buildplateLoadResponse is not null ? Json.Serialize(buildplateLoadResponse) : null;
                            }
                        case "saved":
                            {
                                RequestWithInstanceId<WorldSavedMessage>? requestWithInstanceId = ReadRequest<WorldSavedMessage>(request.data);
                                return requestWithInstanceId is null
                                    ? null
                                    : await HandleSaved(requestWithInstanceId.InstanceId, requestWithInstanceId.Request.dataBase64, request.timestamp) ? "" : null;
                            }
                        case "playerConnected":
                            {
                                Log.Debug("RequestHandler playerConnected");
                                RequestWithInstanceId<PlayerConnectedRequest>? requestWithInstanceId = ReadRequest<PlayerConnectedRequest>(request.data);
                                if (requestWithInstanceId is null)
                                    return null;

                                PlayerConnectedResponse? playerConnectedResponse = HandlePlayerConnected(requestWithInstanceId.InstanceId, requestWithInstanceId.Request);
                                return playerConnectedResponse is not null ? Json.Serialize(playerConnectedResponse) : null;
                            }
                        case "playerDisconnected":
                            {
                                RequestWithInstanceId<PlayerDisconnectedRequest>? requestWithInstanceId = ReadRequest<PlayerDisconnectedRequest>(request.data);
                                if (requestWithInstanceId is null)
                                    return null;

                                PlayerDisconnectedResponse? playerDisconnectedResponse = HandlePlayerDisconnected(requestWithInstanceId.InstanceId, requestWithInstanceId.Request, request.timestamp);
                                return playerDisconnectedResponse is not null ? Json.Serialize(playerDisconnectedResponse) : null;
                            }
                        case "playerDead":
                            {
                                RequestWithInstanceId<string>? requestWithInstanceId = ReadRequest<string>(request.data);
                                if (requestWithInstanceId is null)
                                {
                                    return null;
                                }

                                bool? respawn = HandlePlayerDead(requestWithInstanceId.InstanceId, requestWithInstanceId.Request, request.timestamp);
                                return respawn is not null ? Json.Serialize(respawn.Value) : null;
                            }
                        case "getInitialPlayerState":
                            {
                                RequestWithInstanceId<string>? requestWithInstanceId = ReadRequest<string>(request.data);
                                if (requestWithInstanceId is null)
                                {
                                    return null;
                                }

                                InitialPlayerStateResponse? initialPlayerStateResponse = HandleGetInitialPlayerState(requestWithInstanceId.InstanceId, requestWithInstanceId.Request, request.timestamp);
                                return initialPlayerStateResponse is not null ? Json.Serialize(initialPlayerStateResponse) : null;
                            }
                        case "getInventory":
                            {
                                RequestWithInstanceId<string>? requestWithInstanceId = ReadRequest<string>(request.data);
                                if (requestWithInstanceId is null)
                                    return null;

                                InventoryResponse? inventoryResponse = HandleGetInventory(requestWithInstanceId.InstanceId, requestWithInstanceId.Request);
                                return inventoryResponse is not null ? Json.Serialize(inventoryResponse) : null;
                            }
                        case "inventoryAdd":
                            {
                                RequestWithInstanceId<InventoryAddItemMessage>? requestWithInstanceId = ReadRequest<InventoryAddItemMessage>(request.data);
                                return requestWithInstanceId is null
                                    ? null
                                    : HandleInventoryAdd(requestWithInstanceId.InstanceId, requestWithInstanceId.Request, request.timestamp) ? "" : null;
                            }
                        case "inventoryRemove":
                            {
                                RequestWithInstanceId<InventoryRemoveItemRequest>? requestWithBuildplateId = ReadRequest<InventoryRemoveItemRequest>(request.data);
                                if (requestWithBuildplateId is null)
                                    return null;

                                object response = HandleInventoryRemove(requestWithBuildplateId.InstanceId, requestWithBuildplateId.Request);
                                return response is not null ? Json.Serialize(response) : null;
                            }
                        case "inventoryUpdateWear":
                            {
                                RequestWithInstanceId<InventoryUpdateItemWearMessage>? requestWithInstanceId = ReadRequest<InventoryUpdateItemWearMessage>(request.data);

                                return requestWithInstanceId is null
                                    ? null
                                    : HandleInventoryUpdateWear(requestWithInstanceId.InstanceId, requestWithInstanceId.Request) ? "" : null;
                            }
                        case "inventorySetHotbar":
                            {
                                RequestWithInstanceId<InventorySetHotbarMessage>? requestWithInstanceId = ReadRequest<InventorySetHotbarMessage>(request.data);

                                return requestWithInstanceId is null
                                    ? null
                                    : HandleInventorySetHotbar(requestWithInstanceId.InstanceId, requestWithInstanceId.Request) ? "" : null;
                            }
                        default:
                            return null;
                    }
                }
                catch (EarthDB.DatabaseException ex)
                {
                    Log.Error($"Database error while handling request: {ex}");
                    return null;
                }
            },
            () =>
            {
                Log.Fatal("Buildplates event bus request handler error");
                Log.CloseAndFlush();
                Environment.Exit(1);
            }
        ));
    }

    private sealed record BuildplateLoadRequest(
        string PlayerId,
        string BuildplateId
    );

    private sealed record SharedBuildplateLoadRequest(
        string SharedBuildplateId
    );

    private sealed record EncounterBuildplateLoadRequest(
        string EncounterBuildplateId
    );

    private sealed record BuildplateLoadResponse(
        string ServerDataBase64
    );

    private async Task<BuildplateLoadResponse?> HandleLoad(string playerId, string buildplateId)
    {
        EarthDB.Results results = new EarthDB.Query(false)
            .Get("buildplates", playerId, typeof(Buildplates))
            .Execute(_earthDB);
        Buildplates buildplates = (Buildplates)results.Get("buildplates").Value;

        Buildplates.Buildplate? buildplate = buildplates.getBuildplate(buildplateId);
        if (buildplate is null)
            return null;

        byte[]? serverData = (await _objectStoreClient.get(buildplate.serverDataObjectId).Task) as byte[];
        if (serverData is null)
        {
            Log.Error($"Data object {buildplate.serverDataObjectId} for buildplate {buildplateId} could not be loaded from object store");
            return null;
        }

        string serverDataBase64 = Convert.ToBase64String(serverData);

        return new BuildplateLoadResponse(serverDataBase64);
    }

    private async Task<BuildplateLoadResponse?> HandleLoadShared(string sharedBuildplateId)
    {
        EarthDB.Results results = new EarthDB.Query(false)
            .Get("sharedBuildplates", "", typeof(SharedBuildplates))
            .Execute(_earthDB);
        SharedBuildplates sharedBuildplates = (SharedBuildplates)results.Get("sharedBuildplates").Value;

        SharedBuildplates.SharedBuildplate? sharedBuildplate = sharedBuildplates.getSharedBuildplate(sharedBuildplateId);
        if (sharedBuildplate is null)
        {
            return null;
        }

        byte[]? serverData = (await _objectStoreClient.get(sharedBuildplate.serverDataObjectId).Task) as byte[];
        if (serverData is null)
        {
            Log.Error($"Data object {sharedBuildplate.serverDataObjectId} for shared buildplate {sharedBuildplateId} could not be loaded from object store");
            return null;
        }

        string serverDataBase64 = Convert.ToBase64String(serverData);

        return new BuildplateLoadResponse(serverDataBase64);
    }

    private async Task<BuildplateLoadResponse?> HandleLoadEncounter(string encounterBuildplateId)
    {
        EarthDB.Results results = new EarthDB.Query(false)
            .Get("encounterBuildplates", "", typeof(EncounterBuildplates))
            .Execute(_earthDB);
        EncounterBuildplates encounterBuildplates = (EncounterBuildplates)results.Get("encounterBuildplates").Value;

        EncounterBuildplates.EncounterBuildplate? encounterBuildplate = encounterBuildplates.getEncounterBuildplate(encounterBuildplateId);
        if (encounterBuildplate is null)
        {
            return null;
        }

        byte[]? serverData = (await _objectStoreClient.get(encounterBuildplate.serverDataObjectId).Task) as byte[];
        if (serverData is null)
        {
            Log.Error($"Data object {encounterBuildplate.serverDataObjectId} for encounter buildplate {encounterBuildplateId} could not be loaded from object store");
            return null;
        }

        string serverDataBase64 = Convert.ToBase64String(serverData);

        return new BuildplateLoadResponse(serverDataBase64);
    }

    private async Task<bool> HandleSaved(string instanceId, string dataBase64, long timestamp)
    {
        BuildplateInstancesManager.InstanceInfo? instanceInfo = _buildplateInstancesManager.GetInstanceInfo(instanceId);
        if (instanceInfo is null)
        {
            return false;
        }

        if (instanceInfo.Type != BuildplateInstancesManager.InstanceType.BUILD)
        {
            return false;
        }

        string? playerId = instanceInfo.PlayerId;
        string buildplateId = instanceInfo.BuildplateId;

        Debug.Assert(playerId is not null);

        byte[] serverData;
        try
        {
            serverData = Convert.FromBase64String(dataBase64);
        }
        catch
        {
            return false;
        }

        EarthDB.Results results = new EarthDB.Query(false)
            .Get("buildplates", playerId, typeof(Buildplates))
            .Execute(_earthDB);
        Buildplates.Buildplate? buildplateUnsafeForPreviewGenerator = ((Buildplates)results.Get("buildplates").Value).getBuildplate(buildplateId);
        if (buildplateUnsafeForPreviewGenerator is null)
            return false;

        string? preview = _buildplateInstancesManager.GetBuildplatePreview(serverData, buildplateUnsafeForPreviewGenerator.night);
        if (preview is null)
            Log.Warning("Could not generate preview for buildplate");

        string? serverDataObjectId = (await _objectStoreClient.store(serverData).Task) as string;
        if (serverDataObjectId is null)
        {
            Log.Error($"Could not store new data object for buildplate {buildplateId} in object store");
            return false;
        }

        string? previewObjectId;
        if (preview is not null)
        {
            previewObjectId = (await _objectStoreClient.store(Encoding.ASCII.GetBytes(preview)).Task) as string;
            if (previewObjectId is null)
                Log.Warning($"Could not store new preview object for buildplate {buildplateId} in object store");
        }
        else
            previewObjectId = null;

        try
        {
            EarthDB.Results results1 = new EarthDB.Query(true)
                .Get("buildplates", playerId, typeof(Buildplates))
                .Then(results2 =>
                {
                    Buildplates buildplates = (Buildplates)results2.Get("buildplates").Value;
                    Buildplates.Buildplate? buildplate = buildplates.getBuildplate(buildplateId);
                    if (buildplate is not null)
                    {
                        buildplate.lastModified = timestamp;

                        string oldServerDataObjectId = buildplate.serverDataObjectId;
                        buildplate.serverDataObjectId = serverDataObjectId;

                        string oldPreviewObjectId;
                        if (previewObjectId is not null)
                        {
                            oldPreviewObjectId = buildplate.previewObjectId;
                            buildplate.previewObjectId = previewObjectId;
                        }
                        else
                            oldPreviewObjectId = "";

                        return new EarthDB.Query(true)
                            .Update("buildplates", playerId, buildplates)
                            .Extra("exists", true)
                            .Extra("oldServerDataObjectId", oldServerDataObjectId)
                            .Extra("oldPreviewObjectId", oldPreviewObjectId);
                    }
                    else
                    {
                        return new EarthDB.Query(false)
                            .Extra("exists", false);
                    }
                })
                .Execute(_earthDB);

            bool exists = (bool)results1.getExtra("exists");
            if (exists)
            {
                string oldServerDataObjectId = (string)results1.getExtra("oldServerDataObjectId");
                _objectStoreClient.delete(oldServerDataObjectId);

                string oldPreviewObjectId = (string)results1.getExtra("oldPreviewObjectId");
                if (!string.IsNullOrEmpty(oldPreviewObjectId))
                    _objectStoreClient.delete(oldPreviewObjectId);

                Log.Information($"Stored new snapshot for buildplate {buildplateId}");

                return true;
            }
            else
            {
                _objectStoreClient.delete(serverDataObjectId);
                if (previewObjectId is not null)
                {
                    _objectStoreClient.delete(previewObjectId);
                }

                return false;
            }
        }
        catch (EarthDB.DatabaseException)
        {
            _objectStoreClient.delete(serverDataObjectId);
            if (previewObjectId is not null)
            {
                _objectStoreClient.delete(previewObjectId);
            }

            throw;
        }
    }

    private PlayerConnectedResponse? HandlePlayerConnected(string instanceId, PlayerConnectedRequest playerConnectedRequest)
    {
        // TODO: check join code etc.

        BuildplateInstancesManager.InstanceInfo? instanceInfo = _buildplateInstancesManager.GetInstanceInfo(instanceId);

        if (instanceInfo is null)
        {
            return null;
        }

        InventoryResponse? initialInventoryContents;
        switch (instanceInfo.Type)
        {
            case BuildplateInstancesManager.InstanceType.BUILD:
                {
                    initialInventoryContents = null;
                }

                break;
            case BuildplateInstancesManager.InstanceType.PLAY:
                {
                    EarthDB.Results results = new EarthDB.Query(false)
                        .Get("inventory", playerConnectedRequest.uuid, typeof(Inventory))
                        .Get("hotbar", playerConnectedRequest.uuid, typeof(Hotbar))
                        .Execute(_earthDB);

                    Inventory inventory = (Inventory)results.Get("inventory").Value;
                    Hotbar hotbar = (Hotbar)results.Get("hotbar").Value;

                    initialInventoryContents = new InventoryResponse(
                        [.. Enumerable.Concat(
                            inventory.getStackableItems()
                                .Select(item => new InventoryResponse.Item(item.id, item.count, null, 0)),
                            inventory.getNonStackableItems()
                                .SelectMany(item => item.instances
                                    .Select(instance => new InventoryResponse.Item(item.id, 1, instance.instanceId, instance.wear)))
                        ).Where(item => item.count > 0)],
                        [.. hotbar.items.Select(item => item is { count: > 0 } ? new InventoryResponse.HotbarItem(item.uuid, item.count, item.instanceId) : null)]
                    );
                }

                break;
            case BuildplateInstancesManager.InstanceType.SHARED_BUILD or BuildplateInstancesManager.InstanceType.SHARED_PLAY:

                {
                    EarthDB.Results results = new EarthDB.Query(false)
                        .Get("sharedBuildplates", "", typeof(SharedBuildplates))
                        .Execute(_earthDB);
                    SharedBuildplates sharedBuildplates = (SharedBuildplates)results.Get("sharedBuildplates").Value;
                    SharedBuildplates.SharedBuildplate? sharedBuildplate = sharedBuildplates.getSharedBuildplate(instanceInfo.BuildplateId);
                    if (sharedBuildplate is null)
                    {
                        return null;
                    }

                    initialInventoryContents = new InventoryResponse(
                        [.. Enumerable.Concat(
                            sharedBuildplate.hotbar
                                .Where(item => item is { count: > 0, instanceId: null })
                                .Collect(() => new Dictionary<string, int>(), (hashMap, hotbarItem) =>
                                {
                                    Debug.Assert(hotbarItem is not null);

                                    hashMap[hotbarItem.uuid] = hashMap.GetOrDefault(hotbarItem.uuid, 0) + hotbarItem.count;
                                }, (hashMap1, hashMap2) =>
                                {
                                    foreach (var (uuid, count) in hashMap2)
                                    {
                                        hashMap1[uuid] = hashMap1.GetOrDefault(uuid) + count;
                                    }
                                })
                                .Select(entry => new InventoryResponse.Item(entry.Key, entry.Value, null, 0)),
                            sharedBuildplate.hotbar
                                .Where(item => item is { count: > 0, instanceId: not null })
                                .Select(item => new InventoryResponse.Item(item!.uuid, 1, item.instanceId, item.wear))
                        )],
                        [.. sharedBuildplate.hotbar.Select(item => item is { count: > 0 } ? new InventoryResponse.HotbarItem(item.uuid, item.count, item.instanceId) : null)]
                    );
                }

                break;
            case BuildplateInstancesManager.InstanceType.ENCOUNTER:
                {
                    EarthDB.Results results = new EarthDB.Query(true)
                        .Get("inventory", playerConnectedRequest.uuid, typeof(Inventory))
                        .Get("hotbar", playerConnectedRequest.uuid, typeof(Hotbar))
                        .Then(results1 =>
                        {
                            Inventory inventory = (Inventory)results1.Get("inventory").Value;
                            Hotbar hotbar = (Hotbar)results1.Get("hotbar").Value;

                            var inventoryResponseHotbar = new InventoryResponse.HotbarItem[7];
                            Dictionary<string, int?> inventoryResponseStackableItems = [];
                            LinkedList<InventoryResponse.Item> inventoryResponseNonStackableItems = [];
                            for (int index = 0; index < 7; index++)
                            {
                                Hotbar.Item? item = hotbar.items[index];
                                if (item is not null)
                                {
                                    if (item.instanceId is null)
                                    {
                                        inventory.takeItems(item.uuid, item.count);
                                        inventoryResponseStackableItems[item.uuid] = inventoryResponseStackableItems.GetValueOrDefault(item.uuid, 0) + item.count;
                                        inventoryResponseHotbar[index] = new InventoryResponse.HotbarItem(item.uuid, item.count, null);
                                    }
                                    else
                                    {
                                        int wear = inventory.takeItems(item.uuid, [item.instanceId])![0].wear;
                                        inventoryResponseNonStackableItems.AddLast(new InventoryResponse.Item(item.uuid, 1, item.instanceId, wear));
                                        inventoryResponseHotbar[index] = new InventoryResponse.HotbarItem(item.uuid, 1, item.instanceId);
                                    }
                                }
                            }

                            hotbar.limitToInventory(inventory);

                            InventoryResponse inventoryResponse = new InventoryResponse(
                            [
                                .. inventoryResponseStackableItems.Select(entry => new InventoryResponse.Item(entry.Key, entry.Value, null, 0)),
                                    .. inventoryResponseNonStackableItems
                            ],
                            inventoryResponseHotbar
                        );
                            return new EarthDB.Query(true)
                                .Update("inventory", playerConnectedRequest.uuid, inventory)
                                .Update("hotbar", playerConnectedRequest.uuid, hotbar)
                                .Extra("inventoryResponse", inventoryResponse);
                        })
                        .Execute(_earthDB);

                    initialInventoryContents = (InventoryResponse)results.getExtra("inventoryResponse");
                }

                break;
            default:
                {
                    // shouldn't happen, safe default
                    initialInventoryContents = new InventoryResponse([], new InventoryResponse.HotbarItem[7]);
                }

                break;
        }

        PlayerConnectedResponse playerConnectedResponse = new PlayerConnectedResponse(
            true,
            initialInventoryContents
        );

        return playerConnectedResponse;
    }

    private PlayerDisconnectedResponse? HandlePlayerDisconnected(string instanceId, PlayerDisconnectedRequest playerDisconnectedRequest, long timestamp)
    {
        BuildplateInstancesManager.InstanceInfo? instanceInfo = _buildplateInstancesManager.GetInstanceInfo(instanceId);
        if (instanceInfo is null)
        {
            return null;
        }

        bool usesBackpack = instanceInfo.Type == BuildplateInstancesManager.InstanceType.ENCOUNTER;
        if (usesBackpack)
        {
            InventoryResponse? backpackContents = playerDisconnectedRequest.backpackContents;
            if (backpackContents is null)
            {
                Log.Error("Expected backpack contents in player disconnected request");
                return null;
            }

            EarthDB.Results results = new EarthDB.Query(true)
                .Get("inventory", playerDisconnectedRequest.playerId, typeof(Inventory))
                .Get("journal", playerDisconnectedRequest.playerId, typeof(Journal))
                .Then(results1 =>
                {
                    Inventory inventory = (Inventory)results1.Get("inventory").Value;
                    Journal journal = (Journal)results1.Get("journal").Value;

                    LinkedList<string> unlockedJournalItems = [];
                    foreach (InventoryResponse.Item item in backpackContents.items)
                    {
                        Catalog.ItemsCatalog.Item? catalogItem = _catalog.itemsCatalog.getItem(item.id);
                        if (catalogItem is null)
                        {
                            Log.Error("Backpack contents contained item that is not in item catalog");
                            continue;
                        }

                        if (!catalogItem.stackable && item.instanceId is null)
                        {
                            Log.Error("Backpack contents contained non-stackable item without instance ID");
                            continue;
                        }

                        Debug.Assert(item.count is not null);
                        if (catalogItem.stackable)
                        {
                            inventory.addItems(item.id, item.count.Value);
                        }
                        else
                        {
                            Debug.Assert(item.instanceId is not null);

                            inventory.addItems(item.id, [new NonStackableItemInstance(item.instanceId, item.wear)]);
                        }

                        if (journal.addCollectedItem(item.id, timestamp, item.count.Value) == 0)
                        {
                            if (catalogItem.journalEntry is not null)
                            {
                                unlockedJournalItems.AddLast(item.id);
                            }
                        }

                    }

                    Hotbar hotbar = new Hotbar();
                    for (int index = 0; index < 7; index++)
                    {
                        InventoryResponse.HotbarItem? hotbarItem = backpackContents.hotbar[index];
                        if (hotbarItem is not null)
                        {
                            hotbar.items[index] = new Hotbar.Item(hotbarItem.id, hotbarItem.count, hotbarItem.instanceId);
                        }
                    }

                    hotbar.limitToInventory(inventory);

                    EarthDB.Query query = new EarthDB.Query(true)
                            .Update("inventory", playerDisconnectedRequest.playerId, inventory)
                            .Update("hotbar", playerDisconnectedRequest.playerId, hotbar)
                            .Update("journal", playerDisconnectedRequest.playerId, journal);
                    foreach (string itemId in unlockedJournalItems)
                    {
                        query.Then(TokenUtils.AddToken(playerDisconnectedRequest.playerId, new Tokens.JournalItemUnlockedToken(itemId)));
                    }

                    return query;
                })
                .Execute(_earthDB);
        }

        return new PlayerDisconnectedResponse();
    }

    private static bool? HandlePlayerDead(string instanceId, string playerId, long currentTime)
    {
        BuildplateInstancesManager.InstanceInfo? instanceInfo = _buildplateInstancesManager.GetInstanceInfo(instanceId);
        return instanceInfo is null
            ? null
            : instanceInfo.Type is BuildplateInstancesManager.InstanceType.BUILD or BuildplateInstancesManager.InstanceType.SHARED_BUILD;
    }

    private sealed record EffectInfo(
        long EndTime,
        Catalog.ItemsCatalog.Item.BoostInfo.Effect Effect
    );
    private InitialPlayerStateResponse? HandleGetInitialPlayerState(string instanceId, string playerId, long currentTime)
    {
        BuildplateInstancesManager.InstanceInfo? instanceInfo = _buildplateInstancesManager.GetInstanceInfo(instanceId);

        if (instanceInfo is null)
        {
            return null;
        }

        var (useHealth, useBoosts) = instanceInfo.Type switch
        {
            BuildplateInstancesManager.InstanceType.BUILD => (false, false),
            BuildplateInstancesManager.InstanceType.PLAY => (false, true),
            BuildplateInstancesManager.InstanceType.SHARED_BUILD => (false, false),
            BuildplateInstancesManager.InstanceType.SHARED_PLAY => (false, true),
            BuildplateInstancesManager.InstanceType.ENCOUNTER => (true, true),
            _ => (false, false),
        };

        if (!useHealth && !useBoosts)
        {
            return new InitialPlayerStateResponse(20.0f, []);
        }
        else
        {
            if (!useBoosts)
            {
                throw new UnreachableException();
            }

            EarthDB.Results results = new EarthDB.Query(false)
                .Get("profile", playerId, typeof(Profile))
                .Get("boosts", playerId, typeof(Boosts))
                .Execute(_earthDB);
            Profile profile = (Profile)results.Get("profile").Value;
            Boosts boosts = (Boosts)results.Get("boosts").Value;

            float maxHealth = BoostUtils.GetMaxPlayerHealth(boosts, currentTime, _catalog.itemsCatalog);

            return new InitialPlayerStateResponse(
                useHealth ? float.Min(profile.health, maxHealth) : maxHealth,
                [.. boosts.activeBoosts
                .Where(activeBoost => activeBoost is not null)
                .Where(activeBoost => activeBoost!.startTime + activeBoost.duration >= currentTime)
                .SelectMany(activeBoost => _catalog.itemsCatalog.getItem(activeBoost!.itemId)!.boostInfo!.effects.Select(effect => new EffectInfo(activeBoost.startTime + activeBoost.duration, effect)))
                .Where(effectInfo => effectInfo.Effect.type is CICIBIEType.ADVENTURE_XP or CICIBIEType.DEFENSE or CICIBIEType.EATING or CICIBIEType.HEALTH or CICIBIEType.MINING_SPEED or CICIBIEType.STRENGTH)
                .Select(effectInfo => new InitialPlayerStateResponse.BoostStatusEffect(
                    effectInfo.Effect.type switch
                    {
                        CICIBIEType.ADVENTURE_XP => InitialPlayerStateResponse.BoostStatusEffect.Type.ADVENTURE_XP,
                        CICIBIEType.DEFENSE => InitialPlayerStateResponse.BoostStatusEffect.Type.DEFENSE,
                        CICIBIEType.EATING => InitialPlayerStateResponse.BoostStatusEffect.Type.EATING,
                        CICIBIEType.HEALTH => InitialPlayerStateResponse.BoostStatusEffect.Type.HEALTH,
                        CICIBIEType.MINING_SPEED => InitialPlayerStateResponse.BoostStatusEffect.Type.MINING_SPEED,
                        CICIBIEType.STRENGTH => InitialPlayerStateResponse.BoostStatusEffect.Type.STRENGTH,
                        _ => throw new UnreachableException(),
                    },
                    effectInfo.Effect.value,
                    effectInfo.EndTime - currentTime
                ))]
            );
        }
    }

    private InventoryResponse? HandleGetInventory(string instanceId, string requestedInventoryPlayerId)
    {
        EarthDB.Results results = new EarthDB.Query(false)
            .Get("inventory", requestedInventoryPlayerId, typeof(Inventory))
            .Get("hotbar", requestedInventoryPlayerId, typeof(Hotbar))
            .Execute(_earthDB);
        Inventory inventory = (Inventory)results.Get("inventory").Value;
        Hotbar hotbar = (Hotbar)results.Get("hotbar").Value;

        return new InventoryResponse(
            [.. Enumerable.Concat(
                inventory.getStackableItems()
                    .Select(item => new InventoryResponse.Item(item.id, item.count, null, 0)),
                inventory.getNonStackableItems()
                    .SelectMany(item => item.instances
                    .Select(instance => new InventoryResponse.Item(item.id, 1, instance.instanceId, instance.wear)))
            ).Where(item => item.count > 0)],
            [.. hotbar.items.Select(item => item is not null && item.count > 0 ? new InventoryResponse.HotbarItem(item.uuid, item.count, item.instanceId) : null)]
        );
    }

    private bool HandleInventoryAdd(string instanceId, InventoryAddItemMessage inventoryAddItemMessage, long timestamp)
    {
        Catalog.ItemsCatalog.Item? catalogItem = _catalog.itemsCatalog.getItem(inventoryAddItemMessage.itemId);
        if (catalogItem is null)
            return false;
        if (!catalogItem.stackable && inventoryAddItemMessage.instanceId is null)
            return false;

        EarthDB.Results results = new EarthDB.Query(true)
            .Get("inventory", inventoryAddItemMessage.playerId, typeof(Inventory))
            .Get("journal", inventoryAddItemMessage.playerId, typeof(Journal))
            .Then(results1 =>
            {
                Inventory inventory = (Inventory)results1.Get("inventory").Value;
                Journal journal = (Journal)results1.Get("journal").Value;

                if (catalogItem.stackable)
                    inventory.addItems(inventoryAddItemMessage.itemId, inventoryAddItemMessage.count);
                else
                    inventory.addItems(inventoryAddItemMessage.itemId, [new NonStackableItemInstance(inventoryAddItemMessage.instanceId!, inventoryAddItemMessage.wear)]);

                bool journalItemUnlocked = false;
                if (journal.addCollectedItem(inventoryAddItemMessage.itemId, timestamp, inventoryAddItemMessage.count) == 0)
                {
                    if (catalogItem.journalEntry is not null)
                    {
                        journalItemUnlocked = true;
                    }
                }

                EarthDB.Query query = new EarthDB.Query(true)
                    .Update("inventory", inventoryAddItemMessage.playerId, inventory)
                    .Update("journal", inventoryAddItemMessage.playerId, journal);

                if (journalItemUnlocked)
                {
                    query.Then(TokenUtils.AddToken(inventoryAddItemMessage.playerId, new Tokens.JournalItemUnlockedToken(inventoryAddItemMessage.itemId)));
                }

                return query;
            })
            .Execute(_earthDB);

        return true;
    }

    private object HandleInventoryRemove(string instanceId, InventoryRemoveItemRequest inventoryRemoveItemRequest)
    {
        EarthDB.Results results = new EarthDB.Query(true)
            .Get("inventory", inventoryRemoveItemRequest.playerId, typeof(Inventory))
            .Get("hotbar", inventoryRemoveItemRequest.playerId, typeof(Hotbar))
            .Then(results1 =>
            {
                Inventory inventory = (Inventory)results1.Get("inventory").Value;
                Hotbar hotbar = (Hotbar)results1.Get("hotbar").Value;

                object result;
                if (inventoryRemoveItemRequest.instanceId is not null)
                {
                    if (inventory.takeItems(inventoryRemoveItemRequest.itemId, [inventoryRemoveItemRequest.instanceId]) is null)
                    {
                        Log.Warning($"Buildplate instance {instanceId} attempted to remove item {inventoryRemoveItemRequest.itemId} {inventoryRemoveItemRequest.instanceId} from player {inventoryRemoveItemRequest.playerId} that is not in inventory");
                        result = false;
                    }
                    else
                        result = true;
                }
                else
                {
                    if (inventory.takeItems(inventoryRemoveItemRequest.itemId, inventoryRemoveItemRequest.count))
                        result = inventoryRemoveItemRequest.count;
                    else
                    {
                        int count = inventory.getItemCount(inventoryRemoveItemRequest.itemId);
                        if (!inventory.takeItems(inventoryRemoveItemRequest.itemId, count))
                            count = 0;

                        Log.Warning($"Buildplate instance {instanceId} attempted to remove item {inventoryRemoveItemRequest.itemId} {inventoryRemoveItemRequest.count - count} from player {inventoryRemoveItemRequest.playerId} that is not in inventory");
                        result = count;
                    }
                }

                hotbar.limitToInventory(inventory);

                return new EarthDB.Query(true)
                    .Update("inventory", inventoryRemoveItemRequest.playerId, inventory)
                    .Update("hotbar", inventoryRemoveItemRequest.playerId, hotbar)
                    .Extra("result", result);
            })
            .Execute(_earthDB);

        return results.getExtra("result");
    }

    private bool HandleInventoryUpdateWear(string instanceId, InventoryUpdateItemWearMessage inventoryUpdateItemWearMessage)
    {
        EarthDB.Results results = new EarthDB.Query(true)
            .Get("inventory", inventoryUpdateItemWearMessage.playerId, typeof(Inventory))
            .Then(results1 =>
            {
                Inventory inventory = (Inventory)results1.Get("inventory").Value;

                NonStackableItemInstance? nonStackableItemInstance = inventory.getItemInstance(inventoryUpdateItemWearMessage.itemId, inventoryUpdateItemWearMessage.instanceId);
                if (nonStackableItemInstance is not null)
                {
                    // TODO: make NonStackableItemInstance mutable instead of doing this
                    if (inventory.takeItems(inventoryUpdateItemWearMessage.itemId, [inventoryUpdateItemWearMessage.instanceId]) is null)
                        throw new InvalidOperationException();

                    inventory.addItems(inventoryUpdateItemWearMessage.itemId, [new NonStackableItemInstance(inventoryUpdateItemWearMessage.instanceId, inventoryUpdateItemWearMessage.wear)]);
                }
                else
                    Log.Warning("Buildplate instance {instanceId} attempted to update item wear for item {inventoryUpdateItemWearMessage.itemId()} {inventoryUpdateItemWearMessage.instanceId()} player {inventoryUpdateItemWearMessage.playerId()} that is not in inventory");

                return new EarthDB.Query(true)
                    .Update("inventory", inventoryUpdateItemWearMessage.playerId, inventory);
            })
            .Execute(_earthDB);
        return true;
    }

    private bool HandleInventorySetHotbar(string instanceId, InventorySetHotbarMessage inventorySetHotbarMessage)
    {
        EarthDB.Results results = new EarthDB.Query(true)
            .Get("inventory", inventorySetHotbarMessage.playerId, typeof(Inventory))
            .Then(results1 =>
            {
                Inventory inventory = (Inventory)results1.Get("inventory").Value;

                Hotbar hotbar = new Hotbar();
                for (int index = 0; index < hotbar.items.Length; index++)
                {
                    InventorySetHotbarMessage.Item item = inventorySetHotbarMessage.items[index];
                    hotbar.items[index] = item is not null ? new Hotbar.Item(item.itemId, item.count, item.instanceId) : null;
                }

                hotbar.limitToInventory(inventory);

                return new EarthDB.Query(true)
                    .Update("hotbar", inventorySetHotbarMessage.playerId, hotbar);
            })
            .Execute(_earthDB);
        return true;
    }

    private static RequestWithInstanceId<T>? ReadRequest<T>(string str)
    {
        try
        {
            RequestWithInstanceId<T>? request = Json.Deserialize<RequestWithInstanceId<T>>(str);
            return request;
        }
        catch (Exception ex)
        {
            Log.Error($"Bad JSON in buildplates event bus request: {ex}");
            return null;
        }
    }

    private static T? ReadRawRequest<T>(string str)
    {
        try
        {
            T? request = Json.Deserialize<T>(str);
            return request;
        }
        catch (Exception ex)
        {
            Log.Error($"Bad JSON in buildplates event bus request: {ex}");
            return default;
        }
    }

    private sealed record RequestWithInstanceId<T>(
        string InstanceId,
        T Request
    );
}
