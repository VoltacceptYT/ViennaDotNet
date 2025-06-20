using System.Diagnostics;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.DB.Models.Player.Workshop;
using ViennaDotNet.StaticData;

namespace ViennaDotNet.ApiServer.Utils;

public static class SmeltingCalculator
{
    public static State CalculateState(long currentTime, SmeltingSlot.ActiveJob activeJob, SmeltingSlot.Burning? burning, Catalog catalog)
    {
        Catalog.RecipesCatalog.SmeltingRecipe? recipe = catalog.recipesCatalog.getSmeltingRecipe(activeJob.recipeId);
        Debug.Assert(recipe is not null);

        int totalHeatRequired = recipe.heatRequired * activeJob.totalRounds;
        long totalCompletionTime = activeJob.startTime + CalculateDurationForHeat(totalHeatRequired, burning, activeJob.addedFuel);
        long nextCompletionTime = 0;
        int completedRounds;
        if (activeJob.finishedEarly)
        {
            completedRounds = activeJob.totalRounds;
        }
        else
        {
            for (completedRounds = 0; completedRounds < activeJob.totalRounds; completedRounds++)
            {
                nextCompletionTime = activeJob.startTime + CalculateDurationForHeat(recipe.heatRequired * (completedRounds + 1), burning, activeJob.addedFuel);
                if (nextCompletionTime >= currentTime)
                {
                    break;
                }
            }
        }

        if (completedRounds < activeJob.totalRounds && nextCompletionTime == 0)
        {
            throw new InvalidOperationException();
        }

        int availableRounds = completedRounds - activeJob.collectedRounds;
        bool completed = completedRounds == activeJob.totalRounds;

        InputItem input;
        if (activeJob.input.count != activeJob.totalRounds)
        {
            throw new InvalidOperationException();
        }

        if (activeJob.input.instances.Length > 0)
        {
            if (activeJob.input.instances.Length != activeJob.input.count)
            {
                throw new InvalidOperationException();
            }

            input = new InputItem(activeJob.input.id, activeJob.input.count - completedRounds, ArrayExtensions.CopyOfRange(activeJob.input.instances, completedRounds, activeJob.input.instances.Length));
        }
        else
        {
            input = new InputItem(activeJob.input.id, activeJob.input.count - completedRounds, []);
        }

        int consumedAddedFuelCount = 0;
        long fuelEndTime = completed ? totalCompletionTime : currentTime;
        SmeltingSlot.Fuel currentFuel;
        int currentFuelTotalHeat;
        long burnStartTime;
        long burnEndTime;

        if (burning is not null)
        {
            currentFuel = burning.fuel;
            currentFuelTotalHeat = burning.remainingHeat;
            burnStartTime = activeJob.startTime;
            burnEndTime = burnStartTime + burning.remainingHeat * 1000 / burning.fuel.heatPerSecond;
        }
        else
        {
            if (activeJob.addedFuel is null)
            {
                throw new InvalidOperationException();
            }

            currentFuel = activeJob.addedFuel;
            consumedAddedFuelCount = 1;
            currentFuelTotalHeat = currentFuel.heatPerSecond * currentFuel.burnDuration;
            burnStartTime = activeJob.startTime;
            burnEndTime = burnStartTime + currentFuel.burnDuration * 1000;
        }

        while (burnEndTime < fuelEndTime)
        {
            if (activeJob.addedFuel is null)
            {
                throw new InvalidOperationException();
            }

            totalHeatRequired -= currentFuelTotalHeat;
            currentFuel = activeJob.addedFuel;
            consumedAddedFuelCount++;
            currentFuelTotalHeat = currentFuel.heatPerSecond * currentFuel.burnDuration;
            burnStartTime = burnEndTime;
            burnEndTime = burnStartTime + currentFuel.burnDuration * 1000;
        }

        if (totalHeatRequired < 0)
        {
            throw new InvalidOperationException();
        }

        int remainingHeat;
        if (!completed)
        {
            remainingHeat = (int)(currentFuelTotalHeat * (burnEndTime - fuelEndTime)) / (currentFuel.burnDuration * 1000);
        }
        else
        {
            if (totalHeatRequired > currentFuelTotalHeat)
            {
                throw new InvalidOperationException();
            }

            remainingHeat = currentFuelTotalHeat - totalHeatRequired;
        }

        SmeltingSlot.Fuel? remainingAddedFuel;
        if (activeJob.addedFuel is null)
        {
            if (consumedAddedFuelCount > 0)
            {
                throw new InvalidOperationException();
            }

            remainingAddedFuel = null;
        }
        else
        {
            if (consumedAddedFuelCount > activeJob.addedFuel.item.count)
            {
                throw new InvalidOperationException();
            }

            if (activeJob.addedFuel.item.instances.Length > 0)
            {
                if (activeJob.addedFuel.item.instances.Length != activeJob.addedFuel.item.count)
                {
                    throw new InvalidOperationException();
                }

                remainingAddedFuel = new SmeltingSlot.Fuel(new InputItem(activeJob.addedFuel.item.id, activeJob.addedFuel.item.count - consumedAddedFuelCount, ArrayExtensions.CopyOfRange(activeJob.addedFuel.item.instances, consumedAddedFuelCount, activeJob.addedFuel.item.instances.Length)), activeJob.addedFuel.burnDuration, activeJob.addedFuel.heatPerSecond);
            }
            else
            {
                remainingAddedFuel = new SmeltingSlot.Fuel(new InputItem(activeJob.addedFuel.item.id, activeJob.addedFuel.item.count - consumedAddedFuelCount, []), activeJob.addedFuel.burnDuration, activeJob.addedFuel.heatPerSecond);
            }
        }

        SmeltingSlot.Fuel currentBurningFuel;
        if (consumedAddedFuelCount > 0)
        {
            if (activeJob.addedFuel!.item.instances.Length > 0)
            {
                currentBurningFuel = new SmeltingSlot.Fuel(new InputItem(activeJob.addedFuel.item.id, 1, [activeJob.addedFuel.item.instances[consumedAddedFuelCount - 1]]), activeJob.addedFuel.burnDuration, activeJob.addedFuel.heatPerSecond);
            }
            else
            {
                currentBurningFuel = new SmeltingSlot.Fuel(new InputItem(activeJob.addedFuel.item.id, 1, []), activeJob.addedFuel.burnDuration, activeJob.addedFuel.heatPerSecond);
            }
        }
        else
        {
            currentBurningFuel = currentFuel;
        }

        return new State(
            completedRounds,
            availableRounds,
            activeJob.totalRounds,
            input,
            new State.OutputItem(recipe.output, 1),
            nextCompletionTime,
            totalCompletionTime,
            remainingAddedFuel,
            currentBurningFuel,
            remainingHeat,
            burnStartTime,
            burnEndTime,
            completed
        );
    }

    private static int CalculateDurationForHeat(int requiredHeat, SmeltingSlot.Burning? burning, SmeltingSlot.Fuel? addedFuel)
    {
        int duration = 0;
        if (burning is not null)
        {
            if (burning.remainingHeat >= requiredHeat)
            {
                duration += requiredHeat * 1000 / burning.fuel.heatPerSecond;
                requiredHeat = 0;
            }
            else
            {
                duration += burning.remainingHeat * 1000 / burning.fuel.heatPerSecond;
                requiredHeat -= burning.remainingHeat;
            }
        }

        if (addedFuel is not null)
        {
            for (int count = 0; count < addedFuel.item.count; count++)
            {
                if (requiredHeat < addedFuel.heatPerSecond * addedFuel.burnDuration)
                {
                    duration += requiredHeat * 1000 / addedFuel.heatPerSecond;
                    requiredHeat = 0;
                    break;
                }
                else
                {
                    duration += addedFuel.burnDuration * 1000;
                    requiredHeat -= addedFuel.heatPerSecond * addedFuel.burnDuration;
                }
            }
        }

        if (requiredHeat > 0)
        {
            throw new InvalidOperationException();
        }

        return duration;
    }

    public sealed record State(
        int CompletedRounds,
        int AvailableRounds,
        int TotalRounds,
        InputItem Input,
        State.OutputItem Output,
        long NextCompletionTime,
        long TotalCompletionTime,
        SmeltingSlot.Fuel? RemainingAddedFuel,
        SmeltingSlot.Fuel CurrentBurningFuel,
        int RemainingHeat,
        long BurnStartTime,
        long BurnEndTime,
        bool Completed
    )
    {
        public sealed record OutputItem(
            string Id,
            int Count
        );
    }

    // TODO: make this configurable
    public static FinishPrice CalculateFinishPrice(int remainingTime)
    {
        if (remainingTime < 0)
        {
            throw new ArgumentException(nameof(remainingTime));
        }

        int periods = remainingTime / 10000;
        if (remainingTime % 10000 > 0)
        {
            periods = periods + 1;
        }

        int price = periods * 5;
        int changesAt = (periods - 1) * 10000;
        int validFor = remainingTime - changesAt;

        return new FinishPrice(price, validFor);
    }

    public sealed record FinishPrice(
        int Price,
        int ValidFor
    );

    // TODO: make this configurable
    public static int CalculateUnlockPrice(int slotIndex)
    {
        if (slotIndex < 1 || slotIndex > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(slotIndex));
        }

        return slotIndex * 5;
    }
}
