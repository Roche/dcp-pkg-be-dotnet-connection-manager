using System.Text.Json.Nodes;
using System.Text.Json;

namespace OpenDCP.Framework.Connections.Manager.Helpers
{
    public static class JsonParseHelper
    {
        public static JsonArray? ToJsonArray(dynamic? value)
        {
            return value switch
            {
                JsonArray ja => ja,

                JsonElement je when je.ValueKind == JsonValueKind.Array
                    => JsonArray.Create(je),

                string s
                    => JsonNode.Parse(s) as JsonArray,

                _ => null
            };
        }
    }
}
