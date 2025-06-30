using Microsoft.AspNetCore.Mvc;
using ViennaDotNet.ApiServer.Utils;

namespace ViennaDotNet.ApiServer;

[ApiController]
public abstract class ViennaControllerBase : ControllerBase
{
    [NonAction]
    public ContentResult EarthJson(object results)
        => Json(new EarthApiResponse(results));

    [NonAction]
    public ContentResult EarthJson(object? results, EarthApiResponse.UpdatesResponse? updates)
        => Json(new EarthApiResponse(results, updates));

    [NonAction]
    public ContentResult Json(object value)
        => Content(Common.Json.Serialize(value), "application/json");
}
