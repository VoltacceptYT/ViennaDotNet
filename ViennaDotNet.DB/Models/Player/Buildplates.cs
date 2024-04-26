using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ViennaDotNet.Common.Utils;

namespace ViennaDotNet.DB.Models.Player
{
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class Buildplates
    {
        [JsonProperty]
        private readonly Dictionary<string, Buildplate> buildplates = new();

        public Buildplates()
        {
            // empty
        }

        public void addBuildplate(string id, Buildplate buildplate)
        {
            buildplates[id] = buildplate;
        }

        public Buildplate? getBuildplate(string id)
        {
            return buildplates.GetOrDefault(id, null);
        }

        public record BuildplateEntry(
            string id,
            Buildplate buildplate
        )
        {
        }

        public BuildplateEntry[] getBuildplates()
        {
            return buildplates.Select(entry => new BuildplateEntry(entry.Key, entry.Value)).ToArray();
        }

        [JsonObject(MemberSerialization.OptIn)]
        public sealed class Buildplate
        {
            [JsonProperty]
            public readonly int size;
            [JsonProperty]
            public readonly int offset;
            [JsonProperty]
            public readonly int scale;

            [JsonProperty]
            public readonly bool night;

            [JsonProperty]
            public long lastModified;
            [JsonProperty]
            public string serverDataObjectId;
            [JsonProperty]
            public string previewObjectId;

            public Buildplate(int size, int offset, int scale, bool night, long lastModified, string serverDataObjectId, string previewObjectId)
            {
                this.size = size;
                this.offset = offset;
                this.scale = scale;

                this.night = night;

                this.lastModified = lastModified;
                this.serverDataObjectId = serverDataObjectId;
                this.previewObjectId = previewObjectId;
            }
        }
    }
}
