using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using System.Text;
using ViennaDotNet.ApiServer.Exceptions;
using ViennaDotNet.ApiServer.Types.Buildplates;
using ViennaDotNet.ApiServer.Types.Shop;
using ViennaDotNet.ApiServer.Utils;
using ViennaDotNet.Common;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB;
using ViennaDotNet.ObjectStore.Client;
using ViennaDotNet.StaticData;

namespace ViennaDotNet.ApiServer.Controllers;

[Authorize]
[ApiVersion("1.1")]
[Route("1/api/v{version:apiVersion}/commerce")]
public class ShopController : ControllerBase
{
    private static Catalog catalog => Program.staticData.Catalog;
    private static EarthDB earthDB => Program.DB;
    private static ObjectStoreClient objectStoreClient => Program.objectStore;

    private sealed record StoreItemInfoRequest(string Id, string StoreItemType, uint StreamVersion);

    [HttpPost("storeItemInfo")]
    public async Task<IActionResult> GetStoreItemInfo(CancellationToken cancellationToken)
    {
        var request = await Request.Body.AsJsonAsync<StoreItemInfoRequest[]>(cancellationToken);

        if (request is null || request.Length == 0)
        {
            return Content(Json.Serialize(new EarthApiResponse(Array.Empty<StoreItemInfo>())), "application/json");
        }

        List<StoreItemInfo> result = new(request.Length);

        EarthDB.ObjectResults buildplateResults;
        try
        {
            buildplateResults = await new EarthDB.ObjectQuery(false)
                .GetBuildplates(request.Where(item => item.StoreItemType == "Buildplates").Select(item => item.Id))
                .ExecuteAsync(earthDB, cancellationToken);
        }
        catch (EarthDB.DatabaseException exception)
        {
            throw new ServerErrorException(exception);
        }

        foreach (var item in request)
        {
            switch (item.StoreItemType)
            {
                case "Buildplates":
                    {
                        var buildplate = buildplateResults.GetBuildplate(item.Id);

                        Guid itemId = Guid.Parse(item.Id);
                        StoreItemInfo.StoreItemTypeE storeItemType = Enum.Parse<StoreItemInfo.StoreItemTypeE>(item.StoreItemType);

                        if (buildplate is null)
                        {
                            Log.Warning($"Buildplate with id {item.Id} not found");
                            result.Add(new StoreItemInfo(itemId, storeItemType, StoreItemInfo.StoreItemStatus.NotFound, item.StreamVersion, null, null, null, null, null));
                            break;
                        }

                        byte[]? previewData = (await objectStoreClient.Get(buildplate.PreviewObjectId).Task) as byte[];

                        if (previewData is null)
                        {
                            Log.Warning($"Failed to get preview for buildplate {item.Id}");
                            result.Add(new StoreItemInfo(itemId, storeItemType, StoreItemInfo.StoreItemStatus.NotFound, item.StreamVersion, null, null, null, null, null));
                            break;
                        }

                        string model = Encoding.ASCII.GetString(previewData);

                        var itemFromMap = catalog.ShopCatalog.Items.GetValueOrDefault(itemId);

                        result.Add(new StoreItemInfo(
                            itemId,
                            storeItemType,
                            StoreItemInfo.StoreItemStatus.Found,
                            item.StreamVersion,
                            model,
                            new Offset(0, buildplate.Offset, 0),
                            new Dimension(buildplate.Size, buildplate.Size),
                            itemFromMap?.ItemCounts,
                            itemFromMap?.FeaturedItem));
                    }

                    break;
            }
        }

        return Content(Json.Serialize(new EarthApiResponse(result)), "application/json");
    }
}
