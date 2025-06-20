using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using ViennaDotNet.ApiServer.Exceptions;
using ViennaDotNet.ApiServer.Types.Common;
using ViennaDotNet.ApiServer.Types.Tappables;
using ViennaDotNet.ApiServer.Utils;
using ViennaDotNet.Common;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB;
using ViennaDotNet.DB.Models.Player;
using ViennaDotNet.StaticData;

namespace ViennaDotNet.ApiServer.Controllers;

[Authorize]
[ApiVersion("1.1")]
[Route("1/api/v{version:apiVersion}")]
public class TappablesController : ControllerBase
{
    private static TappablesManager tappablesManager => Program.tappablesManager;
    private static EarthDB earthDB => Program.DB;
    private static StaticData.StaticData staticData => Program.staticData;

    [HttpGet("locations/{lat}/{lon}")]
    public async Task<IActionResult> GetTappables(double lat, double lon, CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId))
            return BadRequest();

        long requestStartedOn = HttpContext.GetTimestamp();

        tappablesManager.NotifyTileActive(playerId, lat, lon);

        TappablesManager.Tappable[] tappables = tappablesManager.GetTappablesAround(lat, lon, 5.0);    // TODO: radius
        TappablesManager.Encounter[] encounters = tappablesManager.GetEncountersAround(lat, lon, 5.0);    // TODO: radius

        try
        {
            EarthDB.Results results = await new EarthDB.Query(false)
                .Get("redeemedTappables", playerId, typeof(RedeemedTappables))
                .ExecuteAsync(earthDB, cancellationToken);
            RedeemedTappables redeemedTappables = (RedeemedTappables)results.Get("redeemedTappables").Value;

            IEnumerable<ActiveLocation> activeLocationTappables = tappables
                .Where(tappable => tappable.SpawnTime + tappable.ValidFor > requestStartedOn && !redeemedTappables.isRedeemed(tappable.Id))
                .Select(tappable => new ActiveLocation(
                    tappable.Id,
                    TappablesManager.LocationToTileId(tappable.Lat, tappable.Lon),
                    new Coordinate(tappable.Lat, tappable.Lon),
                    TimeFormatter.FormatTime(tappable.SpawnTime),
                    TimeFormatter.FormatTime(tappable.SpawnTime + tappable.ValidFor),
                    ActiveLocation.Type.TAPPABLE,
                    tappable.Icon,
                    new ActiveLocation.MetadataR(U.RandomUuid().ToString(), Enum.Parse<Rarity>(tappable.Rarity.ToString())),
                    new ActiveLocation.TappableMetadataR(Enum.Parse<Rarity>(tappable.Rarity.ToString())),
                    null
                ));

            IEnumerable<ActiveLocation> activeLocationEncounters = encounters
                .Where(encounter => encounter.SpawnTime + encounter.ValidFor > requestStartedOn)
                .Select(encounter => new ActiveLocation(
                    encounter.Id,
                    TappablesManager.LocationToTileId(encounter.Lat, encounter.Lon),
                    new Coordinate(encounter.Lat, encounter.Lon),
                    TimeFormatter.FormatTime(encounter.SpawnTime),
                    TimeFormatter.FormatTime(encounter.SpawnTime + encounter.ValidFor),
                    ActiveLocation.Type.ENCOUNTER,
                    encounter.Icon,
                    new ActiveLocation.MetadataR(U.RandomUuid().ToString(), Enum.Parse<Rarity>(encounter.Rarity.ToString())),
                    null,
                    new ActiveLocation.EncounterMetadata(
                        ActiveLocation.EncounterMetadata.EncounterType.SHORT_4X4_PEACEFUL,    // TODO
                                                                                              //UUID.randomUUID().toString(),    // TODO: what is this field for and does it matter what we put here?
                        encounter.Id,
                        encounter.EncounterBuildplateId,
                        ActiveLocation.EncounterMetadata.AnchorStateE.OFF,
                        "",
                        ""
                    )
                ));

            ActiveLocation[] activeLocations = [.. activeLocationTappables, .. activeLocationEncounters];

            string resp = Json.Serialize(new EarthApiResponse(new Dictionary<string, object>()
            {
                { "killSwitchedTileIds", new List<object>() },
                { "activeLocations", activeLocations }
            }));
            return Content(resp, "application/json");
        }
        catch (EarthDB.DatabaseException ex)
        {
            throw new ServerErrorException(ex);
        }
    }

    [HttpPost("tappables/{tileId}")]
    public async Task<IActionResult> RedeemTappable(string tileId, CancellationToken cancellationToken)
    {
        string? playerId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(playerId))
            return BadRequest();

        TappableRequest? tappableRequest = await Request.Body.AsJsonAsync<TappableRequest>(cancellationToken);
        if (tappableRequest is null)
            return BadRequest();

        // request.timestamp
        long requestStartedOn = HttpContext.GetTimestamp();

        TappablesManager.Tappable? tappable = tappablesManager.GetTappableWithId(tappableRequest.Id, tileId);
        if (tappable is null || !tappablesManager.IsTappableValidFor(tappable, requestStartedOn, tappableRequest.PlayerCoordinate.Latitude, tappableRequest.PlayerCoordinate.Longitude))
        {
            return BadRequest();
        }

        try
        {
            EarthDB.Results results = await new EarthDB.Query(true)
                .Get("redeemedTappables", playerId, typeof(RedeemedTappables))
                .Get("boosts", playerId, typeof(Boosts))
                .Then(results1 =>
                {
                    EarthDB.Query query = new EarthDB.Query(true);
                    Boosts boosts = (Boosts)results1.Get("boosts").Value;

                    RedeemedTappables redeemedTappables = (RedeemedTappables)results1.Get("redeemedTappables").Value;

                    if (redeemedTappables.isRedeemed(tappable.Id))
                    {
                        query.Extra("success", false);
                        return query;
                    }

                    int experiencePointsGlobalMultiplier = 0;

                    Dictionary<string, int> experiencePointsPerItemMultiplier = [];
                    foreach (var effect in BoostUtils.GetActiveEffects(boosts, requestStartedOn, staticData.catalog.itemsCatalog))
                    {
                        if (effect.type is Catalog.ItemsCatalog.Item.BoostInfo.Effect.Type.ITEM_XP)
                        {
                            if (effect.applicableItemIds is not null && effect.applicableItemIds.Length > 0)
                            {
                                foreach (string itemId in effect.applicableItemIds)
                                {
                                    experiencePointsPerItemMultiplier[itemId] = experiencePointsPerItemMultiplier.GetValueOrDefault(itemId) + effect.value;
                                }
                            }
                            else
                            {
                                experiencePointsGlobalMultiplier += effect.value;
                            }
                        }
                    }

                    var rewards = new Utils.Rewards();

                    foreach (TappablesManager.Tappable.Item item in tappable.Items)
                    {
                        rewards.addItem(item.Id, item.Count);
                        int experiencePoints = staticData.catalog.itemsCatalog.getItem(item.Id)!.experience.tappable;
                        int experiencePointsMultiplier = experiencePointsGlobalMultiplier + experiencePointsPerItemMultiplier.GetValueOrDefault(item.Id);
                        if (experiencePointsMultiplier > 0)
                        {
                            experiencePoints = (experiencePoints * (experiencePointsMultiplier + 100)) / 100;
                        }

                        rewards.addExperiencePoints(experiencePoints * item.Count);
                    }

                    rewards.addRubies(1); // TODO

                    redeemedTappables.add(tappable.Id, tappable.SpawnTime + tappable.ValidFor);
                    redeemedTappables.prune(requestStartedOn);
                    query.Update("redeemedTappables", playerId, redeemedTappables);
                    query.Then(ActivityLogUtils.AddEntry(playerId, new ActivityLog.TappableEntry(requestStartedOn, rewards.ToDBRewardsModel())));
                    query.Then(rewards.toRedeemQuery(playerId, requestStartedOn, staticData));
                    query.Then(results2 => new EarthDB.Query(false).Extra("success", true).Extra("rewards", rewards));

                    return query;
                })
                .ExecuteAsync(earthDB, cancellationToken);

            if ((bool)results.getExtra("success"))
            {
                string resp = Json.Serialize(new EarthApiResponse(new Dictionary<string, object?>()
                {
                    { "token", new Token(
                        Token.Type.TAPPABLE,
                        [],
                        ((Utils.Rewards) results.getExtra("rewards")).ToApiResponse(),
                        Token.Lifetime.PERSISTENT
                    ) },
                    { "updates", null }
                }, new EarthApiResponse.UpdatesResponse(results)));
                return Content(resp, "application/json");
            }
            else
                return BadRequest();
        }
        catch (EarthDB.DatabaseException ex)
        {
            throw new ServerErrorException(ex);
        }
    }

    [HttpPost("multiplayer/encounters/state")]
    public async Task<IActionResult> EncountersState(CancellationToken cancellationToken)
    {
        var requestedIds = await Request.Body.AsJsonAsync<Dictionary<string, object>>(cancellationToken);

        if (requestedIds is null)
        {
            return BadRequest();
        }

        foreach (var entry in requestedIds)
        {
            if (entry.Value is not string)
            {
                return BadRequest();
            }
        }

        // TODO

        var encounterStates = new Dictionary<string, EncounterState>();
        foreach (var (encounterId, tileId) in requestedIds)
        {
            encounterStates[encounterId] = new EncounterState(EncounterState.ActiveEncounterState.PRISTINE);
        }

        string resp = Json.Serialize(new EarthApiResponse(encounterStates));
        return Content(resp, "application/json");
    }

    private sealed record TappableRequest(
        string Id,
        Coordinate PlayerCoordinate
    );
}
