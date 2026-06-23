using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace PoemPoetry.Data
{
    /// <summary>
    /// Central JSON configuration shared by content loading, repositories, and the
    /// editor content tools. Uses Newtonsoft (com.unity.nuget.newtonsoft-json) so the
    /// same settings serve both local files and a future REST backend.
    /// </summary>
    public static class PoemJson
    {
        public static readonly JsonSerializerSettings Settings = CreateSettings();

        // Compact (single-line) variant for index files where diff-noise matters less.
        public static readonly JsonSerializerSettings Compact = CreateSettings(Formatting.None);

        private static JsonSerializerSettings CreateSettings(Formatting formatting = Formatting.Indented)
        {
            var s = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                Formatting = formatting,
                NullValueHandling = NullValueHandling.Ignore,
            };
            s.Converters.Add(new StringEnumConverter());
            return s;
        }

        public static string Serialize(object value) =>
            JsonConvert.SerializeObject(value, Settings);

        public static string SerializeCompact(object value) =>
            JsonConvert.SerializeObject(value, Compact);

        public static T Deserialize<T>(string json) =>
            JsonConvert.DeserializeObject<T>(json, Settings);
    }
}
