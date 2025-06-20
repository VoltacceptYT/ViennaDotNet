using Serilog;
using ViennaDotNet.StaticData;

namespace ViennaDotNet.ApiServer.Utils;

public static class ItemWear
{
    public static float WearToHealth(string itemId, int wear, Catalog.ItemsCatalog itemsCatalog)
    {
        Catalog.ItemsCatalog.Item? catalogItem = itemsCatalog.getItem(itemId);

        if (catalogItem is null || catalogItem.toolInfo is null)
        {
            Log.Warning("Attempt to get item health for non-tool item {}", itemId);
            return 100.0f;
        }

        return ((catalogItem.toolInfo.maxWear - wear) / (float)catalogItem.toolInfo.maxWear) * 100.0f;
    }
}
