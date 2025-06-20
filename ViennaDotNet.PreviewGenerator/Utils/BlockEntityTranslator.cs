using Serilog;
using SharpNBT;
using ViennaDotNet.PreviewGenerator.BlockEntity;
using ViennaDotNet.PreviewGenerator.NBT;
using ViennaDotNet.PreviewGenerator.Registry;

namespace ViennaDotNet.PreviewGenerator.Utils;

public static class BlockEntityTranslator
{
    public static NbtMap? translateBlockEntity(JavaBlocks.BedrockMapping.BlockEntity blockEntityMapping, BlockEntityInfo? javaBlockEntityInfo)
    {
        switch (blockEntityMapping.type)
        {
            case "bed":
                {
                    NbtMapBuilder builder = NbtMap.builder();
                    builder.putString("id", "Bed");
                    builder.putByte("color", ((JavaBlocks.BedrockMapping.BedBlockEntity)blockEntityMapping).color switch
                    {
                        "white" => 0,
                        "orange" => 1,
                        "magenta" => 2,
                        "light_blue" => 3,
                        "yellow" => 4,
                        "lime" => 5,
                        "pink" => 6,
                        "gray" => 7,
                        "light_gray" => 8,
                        "cyan" => 9,
                        "purple" => 10,
                        "blue" => 11,
                        "brown" => 12,
                        "green" => 13,
                        "red" => 14,
                        "black" => 15,
                        _ => 0
                    });
                    return builder.build();
                }
            case "flower_pot":
                {
                    NbtMapBuilder builder = NbtMap.builder();
                    builder.putString("id", "FlowerPot");
                    NbtMap? contents = ((JavaBlocks.BedrockMapping.FlowerPotBlockEntity)blockEntityMapping).contents;
                    if (contents is not null)
                        builder.putCompound("PlantBlock", contents);

                    return builder.build();
                }
            case "moving_block":
                {
                    NbtMapBuilder builder = NbtMap.builder();

                    builder.putString("id", "MovingBlock");

                    if (javaBlockEntityInfo is null)
                    {
                        Log.Debug("Not sending moving block entity data until server provides data");
                        return null;
                    }

                    CompoundTag javaNbt = javaBlockEntityInfo.Nbt!;

                    if (!javaNbt.ContainsKey("blockStateId"))
                    {
                        Log.Warning("Moving block entity data did not contain numeric block state ID");
                        return null;
                    }

                    int javaBlockId = javaNbt.Get<IntTag>("blockStateId").Value;
                    JavaBlocks.BedrockMapping? bedrockMapping = JavaBlocks.getBedrockMapping(javaBlockId);
                    if (bedrockMapping is null)
                    {
                        Log.Warning($"Moving block entity contained block with no mapping {JavaBlocks.getName(javaBlockId)}");
                        return null;
                    }

                    NbtMapBuilder movingBlockBuilder = NbtMap.builder();
                    movingBlockBuilder.putString("name", BedrockBlocks.getName(bedrockMapping.id));
                    movingBlockBuilder.putCompound("states", BedrockBlocks.getStateNbt(bedrockMapping.id));
                    builder.putCompound("movingBlock", movingBlockBuilder.build());

                    if (bedrockMapping.waterlogged)
                    {
                        NbtMapBuilder movingBlockExtraBuilder = NbtMap.builder();
                        movingBlockExtraBuilder.putString("name", BedrockBlocks.getName(BedrockBlocks.WATER));
                        movingBlockExtraBuilder.putCompound("states", BedrockBlocks.getStateNbt(BedrockBlocks.WATER));
                        builder.putCompound("movingBlockExtra", movingBlockExtraBuilder.build());
                    }

                    if (bedrockMapping.blockEntity is not null)
                    {
                        NbtMap? blockEntityNbt = BlockEntityTranslator.translateBlockEntity(bedrockMapping.blockEntity, null);
                        if (blockEntityNbt is not null)
                            builder.putCompound("movingEntity", blockEntityNbt.toBuilder().putInt("x", javaBlockEntityInfo.X).putInt("y", javaBlockEntityInfo.Y).putInt("z", javaBlockEntityInfo.Z).putBoolean("isMovable", false).build());
                    }

                    if (!javaNbt.ContainsKey("basePos"))
                    {
                        Log.Warning("Moving block entity data did not contain piston base position");
                        return null;
                    }

                    CompoundTag basePosTag = javaNbt.Get<CompoundTag>("basePos");
                    builder.putInt("pistonPosX", basePosTag.Get<IntTag>("x").Value);
                    builder.putInt("pistonPosY", basePosTag.Get<IntTag>("y").Value);
                    builder.putInt("pistonPosZ", basePosTag.Get<IntTag>("z").Value);

                    return builder.build();
                }
            case "piston":
                {
                    JavaBlocks.BedrockMapping.PistonBlockEntity pistonBlockEntity = (JavaBlocks.BedrockMapping.PistonBlockEntity)blockEntityMapping;
                    NbtMapBuilder builder = NbtMap.builder();
                    builder.putString("id", "PistonArm");
                    builder.putByte("State", (byte)(pistonBlockEntity.extended ? 2 : 0));
                    builder.putByte("NewState", (byte)(pistonBlockEntity.extended ? 2 : 0));
                    builder.putFloat("Progress", pistonBlockEntity.extended ? 1.0f : 0.0f);
                    builder.putFloat("LastProgress", pistonBlockEntity.extended ? 1.0f : 0.0f);
                    builder.putBoolean("Sticky", pistonBlockEntity.sticky);
                    return builder.build();
                }
            default:
                throw new InvalidOperationException();
        }
    }
}
