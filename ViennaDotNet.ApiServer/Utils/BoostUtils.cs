using System.Diagnostics;
using ViennaDotNet.ApiServer.Types.Common;
using ViennaDotNet.DB.Models.Player;
using ViennaDotNet.StaticData;

using CICIBIEActivation = ViennaDotNet.StaticData.Catalog.ItemsCatalog.Item.BoostInfo.Effect.Activation;
using CICIBIEType = ViennaDotNet.StaticData.Catalog.ItemsCatalog.Item.BoostInfo.Effect.Type;

namespace ViennaDotNet.ApiServer.Utils;

public static class BoostUtils
{
    public static Catalog.ItemsCatalog.Item.BoostInfo.Effect[] GetActiveEffects(Boosts boosts, long currentTime, Catalog.ItemsCatalog itemsCatalog)
    {
        Dictionary<string, Catalog.ItemsCatalog.Item.BoostInfo> activeBoostsInfo = [];
        foreach (var activeBoost in boosts.activeBoosts)
        {
            if (activeBoost is null)
            {
                continue;
            }

            if (activeBoost.startTime + activeBoost.duration < currentTime)
            {
                continue;
            }

            Catalog.ItemsCatalog.Item? item = itemsCatalog.getItem(activeBoost.itemId);
            if (item is null || item.boostInfo is null)
            {
                continue;
            }

            Catalog.ItemsCatalog.Item.BoostInfo? existingBoostInfo = activeBoostsInfo.GetValueOrDefault(item.boostInfo.name);
            if (existingBoostInfo is not null && existingBoostInfo.level > item.boostInfo.level)
            {
                continue;
            }

            activeBoostsInfo[item.boostInfo.name] = item.boostInfo;
        }

        LinkedList<Catalog.ItemsCatalog.Item.BoostInfo.Effect> effects = [];
        foreach (Catalog.ItemsCatalog.Item.BoostInfo boostInfo in activeBoostsInfo.Values)
        {
            foreach (var effect in boostInfo.effects
                .Where(effect => effect.activation switch
                {
                    CICIBIEActivation.INSTANT => false,
                    CICIBIEActivation.TRIGGERED => true,
                    CICIBIEActivation.TIMED => true, // already filtered for expiry time above
                    _ => throw new UnreachableException(),
                }))
            {
                effects.AddLast(effect);
            }
        }

        return [.. effects];
    }

    public sealed record StatModiferValues(
        int MaxPlayerHealthMultiplier,
        int AttackMultiplier,
        int DefenseMultiplier,
        int FoodMultiplier,
        int MiningSpeedMultiplier,
        int CraftingSpeedMultiplier,
        int SmeltingSpeedMultiplier,
        int TappableInteractionRadiusExtraMeters,
        bool KeepHotbar,
        bool KeepInventory,
        bool KeepXp
    );

    public static StatModiferValues GetActiveStatModifiers(Boosts boosts, long currentTime, Catalog.ItemsCatalog itemsCatalog)
    {
        int maxPlayerHealth = 0;
        int attackMultiplier = 0;
        int defenseMultiplier = 0;
        int foodMultiplier = 0;
        int miningSpeedMultiplier = 0;
        int craftingMultiplier = 0;
        int smeltingMultiplier = 0;
        int tappableInteractionRadius = 0;
        bool keepHotbar = false;
        bool keepInventory = false;
        bool keepXp = false;

        foreach (var effect in BoostUtils.GetActiveEffects(boosts, currentTime, itemsCatalog))
        {
            switch (effect.type)
            {
                case CICIBIEType.HEALTH:
                    maxPlayerHealth += effect.value;
                    break;
                case CICIBIEType.STRENGTH:
                    attackMultiplier += effect.value;
                    break;
                case CICIBIEType.DEFENSE:
                    defenseMultiplier += effect.value;
                    break;
                case CICIBIEType.EATING:
                    foodMultiplier += effect.value;
                    break;
                case CICIBIEType.MINING_SPEED:
                    miningSpeedMultiplier += effect.value;
                    break;
                case CICIBIEType.CRAFTING:
                    craftingMultiplier += effect.value;
                    break;
                case CICIBIEType.SMELTING:
                    smeltingMultiplier += effect.value;
                    break;
                case CICIBIEType.TAPPABLE_RADIUS:
                    tappableInteractionRadius += effect.value;
                    break;
                case CICIBIEType.RETENTION_HOTBAR:
                    keepHotbar = true;
                    break;
                case CICIBIEType.RETENTION_BACKPACK:
                    keepInventory = true;
                    break;
                case CICIBIEType.RETENTION_XP:
                    keepXp = true;
                    break;
            }
        }

        return new StatModiferValues(
            maxPlayerHealth,
            attackMultiplier,
            defenseMultiplier,
            foodMultiplier,
            miningSpeedMultiplier,
            craftingMultiplier,
            smeltingMultiplier,
            tappableInteractionRadius,
            keepHotbar,
            keepInventory,
            keepXp
        );
    }

    public static int GetMaxPlayerHealth(Boosts boosts, long currentTime, Catalog.ItemsCatalog itemsCatalog)
        => 20 + (20 * BoostUtils.GetActiveStatModifiers(boosts, currentTime, itemsCatalog).MaxPlayerHealthMultiplier) / 100;

    public static Effect BoostEffectToApiResponse(Catalog.ItemsCatalog.Item.BoostInfo.Effect effect, long boostDuration)
    {
        string effectTypeString = effect.type switch
        {
            CICIBIEType.ADVENTURE_XP => "ItemExperiencePoints",
            CICIBIEType.CRAFTING => "CraftingSpeed",
            CICIBIEType.DEFENSE => "PlayerDefense",
            CICIBIEType.EATING => "FoodHealth",
            CICIBIEType.HEALING => "Health",
            CICIBIEType.HEALTH => "MaximumPlayerHealth",
            CICIBIEType.ITEM_XP => "ItemExperiencePoints",
            CICIBIEType.MINING_SPEED => "BlockDamage",
            CICIBIEType.RETENTION_BACKPACK => "RetainBackpack",
            CICIBIEType.RETENTION_HOTBAR => "RetainHotbar",
            CICIBIEType.RETENTION_XP => "RetainExperiencePoints",
            CICIBIEType.SMELTING => "SmeltingFuelIntensity",
            CICIBIEType.STRENGTH => "AttackDamage",
            CICIBIEType.TAPPABLE_RADIUS => "TappableInteractionRadius",
            _ => throw new UnreachableException(),
        };

        string activationString = effect.activation switch
        {
            CICIBIEActivation.INSTANT => "Instant",
            CICIBIEActivation.TIMED => "Timed",
            CICIBIEActivation.TRIGGERED => "Triggered",
            _ => throw new UnreachableException(),
        };

        return new Effect(
            effectTypeString,
            effect.activation == CICIBIEActivation.TIMED ? TimeFormatter.FormatDuration(boostDuration) : null,
            effect.type == CICIBIEType.RETENTION_BACKPACK || effect.type == CICIBIEType.RETENTION_HOTBAR || effect.type == CICIBIEType.RETENTION_XP ? null : effect.value,
            effect.type switch
            {
                CICIBIEType.HEALING or CICIBIEType.TAPPABLE_RADIUS => "Increment",
                CICIBIEType.ADVENTURE_XP or CICIBIEType.CRAFTING or CICIBIEType.DEFENSE or CICIBIEType.EATING or CICIBIEType.HEALTH or CICIBIEType.ITEM_XP or CICIBIEType.MINING_SPEED or CICIBIEType.SMELTING or CICIBIEType.STRENGTH => "Percentage",
                CICIBIEType.RETENTION_BACKPACK or CICIBIEType.RETENTION_HOTBAR or CICIBIEType.RETENTION_XP => null,
                _ => throw new UnreachableException(),
            },
            effect.type == CICIBIEType.CRAFTING || effect.type == CICIBIEType.SMELTING ? "UtilityBlock" : "Player",
            effect.applicableItemIds,

            effect.type switch
            {
                CICIBIEType.ITEM_XP => ["Tappable"],
                CICIBIEType.ADVENTURE_XP => ["Encounter"],
                _ => [],
            },
            activationString,
            effect.type == CICIBIEType.EATING ? "Health" : null
        );
    }
}
