using Serilog;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using ViennaDotNet.Common.Utils;
using ViennaDotNet.PreviewGenerator.NBT;
using ViennaDotNet.PreviewGenerator.Utils;

namespace ViennaDotNet.PreviewGenerator.Registry;

public static class JavaBlocks
{
    private static readonly Dictionary<int, string> map = [];
    private static readonly Dictionary<string, LinkedList<string>> nonVanillaStatesList = [];

    private static readonly Dictionary<int, BedrockMapping> bedrockMap = [];
    private static readonly Dictionary<string, BedrockMapping> bedrockMapByName = [];
    private static readonly Dictionary<string, BedrockMapping> bedrockNonVanillaMap = [];

    static JavaBlocks()
    {
        DataFile.Load("./registry/blocks_java.json", jToken =>
        {
            JsonArray jArray = (JsonArray)jToken;

            foreach (var _element in jArray)
            {
                JsonObject? element = _element as JsonObject;
                Debug.Assert(element is not null);

                int id = element["id"]!.GetValue<int>();
                string name = element["name"]!.GetValue<string>()!;
                if (map.ContainsKey(id))
                    Log.Warning($"Duplicate Java block ID {id}");
                else
                    map.Add(id, name);

                try
                {
                    BedrockMapping? bedrockMapping = readBedrockMapping((JsonObject)element["bedrock"]!, jArray);
                    if (bedrockMapping is null)
                    {
                        Log.Debug($"Ignoring Java block {name}");
                        continue;
                    }

                    bedrockMap[id] = bedrockMapping;
                    bedrockMapByName[name] = bedrockMapping;
                }
                catch (BedrockMappingFailException ex)
                {
                    Log.Warning($"Cannot find Bedrock block for Java block {name}: {ex.Message}");
                }
            }
        });

        DataFile.Load("./registry/blocks_java_nonvanilla.json", jToken =>
        {
            JsonArray jArray = (JsonArray)jToken;

            foreach (var _element in jArray)
            {
                JsonObject? element = _element as JsonObject;
                Debug.Assert(element is not null);

                string baseName = element["name"]!.GetValue<string>()!;

                LinkedList<string> stateNames = new();
                JsonArray statesArray = (JsonArray)element["states"]!;
                foreach (var _stateElement in statesArray)
                {
                    JsonObject? stateElement = _stateElement as JsonObject;
                    Debug.Assert(stateElement is not null);

                    string stateName = stateElement["name"]!.GetValue<string>()!;
                    stateNames.AddLast(stateName);

                    string name = baseName + stateName;

                    try
                    {
                        BedrockMapping? bedrockMapping = readBedrockMapping((JsonObject)stateElement["bedrock"]!, null);
                        if (bedrockMapping is null)
                        {
                            Log.Debug($"Ignoring Java block {name}");
                            continue;
                        }

                        bedrockNonVanillaMap[name] = bedrockMapping;
                    }
                    catch (BedrockMappingFailException ex)
                    {
                        Log.Warning($"Cannot find Bedrock block for Java block {name}: {ex.Message}");
                    }
                }

                if (nonVanillaStatesList.ContainsKey(baseName))
                    Log.Warning($"Duplicate Java non-vanilla block name {baseName}");
                else
                    nonVanillaStatesList.Add(baseName, stateNames);
            }
        });
    }

    private static BedrockMapping? readBedrockMapping(JsonObject bedrockMappingObject, JsonArray? javaBlocksArray)
    {
        if (bedrockMappingObject.TryGetPropertyValue("ignore", out var ignoreToken) && ignoreToken!.GetValue<bool>())
            return null;

        string name = bedrockMappingObject["name"]!.GetValue<string>()!;

        SortedDictionary<string, object> state = [];
        if (bedrockMappingObject.TryGetPropertyValue("state", out var stateToken))
        {
            JsonObject? stateObject = stateToken as JsonObject;
            Debug.Assert(stateObject is not null);

            foreach (var entry in stateObject)
            {
                JsonValue? stateElement = entry.Value as JsonValue;
                Debug.Assert(stateElement is not null);
                var stateElementType = stateElement.GetValueKind();
                if (stateElementType == JsonValueKind.String)
                    state[entry.Key] = stateElement.GetValue<string>()!;
                else if (stateElementType == JsonValueKind.True)
                    state[entry.Key] = 1;
                else if (stateElementType == JsonValueKind.False)
                    state[entry.Key] = 0;
                else
                    state[entry.Key] = stateElement.GetValue<int>();
            }
        }

        int id = BedrockBlocks.getId(name, state);
        if (id == -1)
        {
            throw new BedrockMappingFailException("Cannot find Bedrock block with provided name and state");
        }

        bool waterlogged = bedrockMappingObject.TryGetPropertyValue("waterlogged", out var waterloggedToken) && waterloggedToken!.GetValue<bool>();

        BedrockMapping.BlockEntity? blockEntity = null;
        if (bedrockMappingObject.TryGetPropertyValue("block_entity", out var blockEntityToken))
        {
            JsonObject? blockEntityObject = blockEntityToken as JsonObject;
            Debug.Assert(blockEntityObject is not null);

            string type = blockEntityObject["type"]!.GetValue<string>()!;
            switch (type)
            {
                case "bed":
                    {
                        string color = blockEntityObject["color"]!.GetValue<string>()!;
                        blockEntity = new BedrockMapping.BedBlockEntity(type, color);
                    }

                    break;
                case "flower_pot":
                    {
                        NbtMap? contents = null;
                        if (blockEntityObject.TryGetPropertyValue("contents", out var contentsToken) && contentsToken!.GetValueKind() is not JsonValueKind.Null)
                        {
                            string contentsName = contentsToken.GetValue<string>()!;
                            if (javaBlocksArray is not null)
                            {
                                contents = javaBlocksArray
                                    .Where(element => ((JsonObject)element!)["name"]!.GetValue<string>() == contentsName)
                                    .Select(element => (JsonObject)((JsonObject)element!)["bedrock"]!)
                                    .Where(element => !element.ContainsKey("ignore") || !element["ignore"]!.GetValue<bool>())
                                    .FirstOrDefault()!.Map(element =>
                                    {
                                        NbtMapBuilder builder = NbtMap.builder();
                                        builder.putString("name", element["name"]!.GetValue<string>()!);
                                        if (element.TryGetPropertyValue("state", out var stateToken))
                                        {
                                            Debug.Assert(stateToken is not null);

                                            NbtMapBuilder stateBuilder = NbtMap.builder();
                                            ((JsonObject)stateToken).ForEach((key, stateElement) =>
                                            {
                                                Debug.Assert(stateElement is not null);

                                                var stateElementType = stateElement.GetValueKind();
                                                if (stateElementType == JsonValueKind.String)
                                                    stateBuilder.putString(key, stateElement.GetValue<string>()!);
                                                else if (stateElementType == JsonValueKind.True)
                                                    stateBuilder.putInt(key, 1);
                                                else if (stateElementType == JsonValueKind.False)
                                                    stateBuilder.putInt(key, 0);
                                                else
                                                    stateBuilder.putInt(key, stateElement.GetValue<int>());
                                            });
                                            builder.putCompound("states", stateBuilder.build());
                                        }

                                        return builder.build();
                                    });
                            }

                            if (contents is null)
                                throw new BedrockMappingFailException("Could not find contents for flower pot");
                        }

                        blockEntity = new BedrockMapping.FlowerPotBlockEntity(type, contents);
                    }

                    break;
                case "moving_block":
                    {
                        blockEntity = new BedrockMapping.BlockEntity(type);
                    }

                    break;
                case "piston":
                    {
                        bool sticky = blockEntityObject["sticky"]!.GetValue<bool>();
                        bool extended = blockEntityObject["extended"]!.GetValue<bool>();
                        blockEntity = new BedrockMapping.PistonBlockEntity(type, sticky, extended);
                    }

                    break;
            }
        }

        BedrockMapping.ExtraData? extraData = null;
        if (bedrockMappingObject.TryGetPropertyValue("extra_data", out var extra_dataToken))
        {
            JsonObject? extraDataObject = extra_dataToken as JsonObject;
            Debug.Assert(extraDataObject is not null);

            string type = extraDataObject["type"]!.GetValue<string>();
            switch (type)
            {
                case "note_block":
                    {
                        int pitch = extraDataObject["pitch"]!.GetValue<int>();
                        extraData = new BedrockMapping.NoteBlockExtraData(pitch);
                    }

                    break;
            }
        }

        return new BedrockMapping(id, waterlogged, blockEntity, extraData);
    }

    private sealed class BedrockMappingFailException : Exception
    {
        public BedrockMappingFailException(string? message)
            : base(message)
        {
        }
    }

    public static int getMaxVanillaBlockId()
    {
        if (map.Count == 0) return -1;
        else return map.Keys.Max();
    }

    public static string[]? getStatesForNonVanillaBlock(string name)
    {
        LinkedList<string>? states = nonVanillaStatesList.GetOrDefault(name, null);
        return states?.ToArray();
    }

    [Obsolete]
    public static string? getName(int id)
    {
        return getName(id, null);
    }

    [Obsolete]
    public static BedrockMapping? getBedrockMapping(int javaId)
    {
        return getBedrockMapping(javaId, null);
    }

    public static string? getName(int id, /*FabricRegistryManager?*/object? fabricRegistryManager)
    {
        string? name = map.GetOrDefault(id, null);
        if (name is null && fabricRegistryManager is not null)
            name = null;//fabricRegistryManager.getBlockName(id);

        return name;
    }

    public static BedrockMapping? getBedrockMapping(int javaId, /*FabricRegistryManager?*/object? fabricRegistryManager)
    {
        BedrockMapping? bedrockMapping = bedrockMap.GetOrDefault(javaId, null);
        if (bedrockMapping is null && fabricRegistryManager is not null)
        {
            string? fabricName = null;//fabricRegistryManager.getBlockName(javaId);
            if (fabricName is not null)
                bedrockMapping = bedrockNonVanillaMap.GetOrDefault(fabricName, null);
        }

        return bedrockMapping;
    }

    public static BedrockMapping? getBedrockMapping(string javaName)
    {
        BedrockMapping? bedrockMapping = bedrockMapByName.GetOrDefault(javaName, null);
        if (bedrockMapping is null)
            bedrockMapping = bedrockNonVanillaMap.GetOrDefault(javaName, null);

        return bedrockMapping;
    }

    public sealed class BedrockMapping
    {
        public readonly int id;
        public readonly bool waterlogged;
        public readonly BlockEntity? blockEntity;
        public readonly ExtraData? extraData;

        public BedrockMapping(int id, bool waterlogged, BlockEntity? blockEntity, ExtraData? extraData)
        {
            this.id = id;
            this.waterlogged = waterlogged;
            this.blockEntity = blockEntity;
            this.extraData = extraData;
        }

        public class BlockEntity
        {
            public readonly string type;

            public BlockEntity(string type)
            {
                this.type = type;
            }
        }

        public class BedBlockEntity : BlockEntity
        {
            public readonly string color;

            public BedBlockEntity(string type, string color)
                : base(type)
            {
                this.color = color;
            }
        }

        public class FlowerPotBlockEntity : BlockEntity
        {
            public readonly NbtMap? contents;

            public FlowerPotBlockEntity(string type, NbtMap? contents)
                : base(type)
            {
                this.contents = contents;
            }
        }

        public class PistonBlockEntity : BlockEntity
        {
            public readonly bool sticky;
            public readonly bool extended;

            public PistonBlockEntity(string type, bool sticky, bool extended)
                : base(type)
            {
                this.sticky = sticky;
                this.extended = extended;
            }
        }

        public abstract class ExtraData
        {
            protected ExtraData()
            {
                // empty
            }
        }

        public class NoteBlockExtraData : ExtraData
        {
            public readonly int pitch;

            public NoteBlockExtraData(int pitch)
                : base()
            {
                this.pitch = pitch;
            }
        }
    }
}
