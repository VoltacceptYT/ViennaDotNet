using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Serilog;
using System;
using System.Runtime.Serialization;
using ViennaDotNet.Common;
using ViennaDotNet.Common.Utils;

namespace ViennaDotNet.TappablesGenerator;

public class TappableGenerator
{
    // TODO: make these configurable
    private static readonly int MIN_COUNT = 1;
    private static readonly int MAX_COUNT = 3;
    private static readonly long MIN_DURATION = 2 * 60 * 1000;
    private static readonly long MAX_DURATION = 5 * 60 * 1000;
    private static readonly long MIN_DELAY = 1 * 60 * 1000;
    private static readonly long MAX_DELAY = 2 * 60 * 1000;

    internal sealed record TappableConfig(
        string icon,
        int experiencePoints,    // TODO: how is tappable XP determined?
        TappableConfig.DropSet[] dropSets,
        Dictionary<string, TappableConfig.ItemCount> itemCounts
    )
    {
        public sealed record DropSet(
            string[] items,
            int chance
        );

        public sealed record ItemCount(
            int min,
            int max
        );
    }

    private readonly TappableConfig[] tappableConfigs;

    private readonly Random random;

    public TappableGenerator()
    {
        try
        {
            Log.Information("Loading tappable generator data");
            string dataDir = Path.Combine("data", "tappable");
            LinkedList<TappableConfig> tappableConfigs = new();
            foreach (string file in Directory.EnumerateFiles(dataDir))
            {
                tappableConfigs.AddLast(JsonConvert.DeserializeObject<TappableConfig>(File.ReadAllText(file))!);
            }

            this.tappableConfigs = [.. tappableConfigs];
        }
        catch (Exception ex)
        {
            Log.Fatal($"Failed to load tappable generator data: {ex}");
            Environment.Exit(1);
            throw new InvalidOperationException();
        }

        Log.Information("Loaded tappable generator data");

        if (tappableConfigs.Length == 0)
        {
            Log.Fatal("No tappable configs provided");
            Environment.Exit(1);
            throw new InvalidOperationException();
        }

        foreach (TappableConfig tappableConfig in tappableConfigs)
        {
            if (tappableConfig.dropSets.Length == 0)
                Log.Warning($"Tappable config {tappableConfig.icon} has no drop sets");

            foreach (string? itemId in tappableConfig.dropSets
                 .SelectMany(dropSet => dropSet.items))
            {
                if (!tappableConfig.itemCounts.ContainsKey(itemId))
                {
                    Log.Fatal($"Tappable config {tappableConfig.icon} has no item count for item {itemId}");
                    Environment.Exit(1);
                    throw new InvalidOperationException();
                }
            }
        }

        random = new Random();
    }

    public long getMaxTappableLifetime()
    {
        return MAX_DELAY + MAX_DURATION + 30 * 1000;
    }

    public Tappable[] generateTappables(int tileX, int tileY, long currentTime)
    {
        LinkedList<Tappable> tappables = new();
        for (int count = random.Next(MIN_COUNT, MAX_COUNT + 1); count > 0; count--)
        {
            long spawnDelay = random.NextInt64(MIN_DELAY, MAX_DELAY + 1);
            long duration = random.NextInt64(MIN_DURATION, MAX_DURATION + 1);

            TappableConfig tappableConfig = tappableConfigs[random.Next(0, tappableConfigs.Length)];

            float[] tileBounds = getTileBounds(tileX, tileY);
            float lat = random.NextSingle(tileBounds[1], tileBounds[0]);
            float lon = random.NextSingle(tileBounds[2], tileBounds[3]);

            int dropSetIndex = random.Next(0, tappableConfig.dropSets.Select(dropSet=>(int)dropSet.chance).Sum());
            TappableConfig.DropSet? dropSet = null;

            foreach (TappableConfig.DropSet dropSet1 in tappableConfig.dropSets)
            {
                dropSet = dropSet1;
                dropSetIndex -= dropSet1.chance;
                if (dropSetIndex <= 0)
                {
                    break;
                }
            }

            if (dropSet == null)
            {
                throw new InvalidOperationException();
            }

            LinkedList<Tappable.Drops.Item> items = new();

            foreach (string itemId in dropSet.items)
            {
                TappableConfig.ItemCount itemCount = tappableConfig.itemCounts[itemId];
                items.AddLast(new Tappable.Drops.Item(itemId, random.Next(itemCount.min, itemCount.max + 1)));
            }

            Tappable.Drops drops = new Tappable.Drops(
                tappableConfig.experiencePoints,
                [.. items]
            );

            Tappable tappable = new Tappable(
                U.RandomUuid().ToString(),
                lat,
                lon,
                currentTime + spawnDelay,
                duration,
                tappableConfig.icon,
                Tappable.Rarity.COMMON,    // TODO: determine rarity from drops
                drops
            );
            tappables.AddLast(tappable);
        }

        return [.. tappables];
    }

    private static float[] getTileBounds(int tileX, int tileY)
    {
        return [
            yToLat((float) tileY / (1 << 16)),
            yToLat((float) (tileY + 1) / (1 << 16)),
            xToLon((float) tileX / (1 << 16)),
            xToLon((float) (tileX + 1) / (1 << 16))
    ];
    }

    private static float xToLon(float x)
    {
        return (float)MathE.ToDegrees((x * 2.0 - 1.0) * Math.PI);
    }

    private static float yToLat(float y)
    {
        return (float)MathE.ToDegrees(Math.Atan(Math.Sinh((1.0 - y * 2.0) * Math.PI)));
    }
}