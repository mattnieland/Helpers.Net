using Helpers.Net.IO.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;

namespace Helpers.Net.Extensions
{
    public static class JsonExtensions
    {
        public static JsonSerializerSettings serializerSetttings = new JsonSerializerSettings
        {
            Converters = new List<JsonConverter> { new BetterJsonConverter() },
            Formatting = Formatting.Indented,
            FloatParseHandling = FloatParseHandling.Decimal
        };
        public static JsonSerializer serializer = JsonSerializer.Create(serializerSetttings);
        public static IDictionary<string, object> ToDictionary(this JObject json, params string[] excludeFields)
        {
            var propertyValuePairs = json.ToObject<Dictionary<string, object>>(serializer);
            ProcessJObjectProperties(propertyValuePairs, excludeFields);
            ProcessJArrayProperties(propertyValuePairs, excludeFields);
            return propertyValuePairs;
        }

        public static IDictionary<string, object> ToDictionary(this JArray json, params string[] excludeFields)
        {
            var obj = new JObject();
            obj["Array"] = json;
            return ToDictionary(obj, excludeFields);
        }
        public static IDictionary<string, object> AsDictionary(this object obj, JsonSerializer jsonSerializer = null, params string[] excludeFields)
        {
            if (jsonSerializer != null)
                return JObject.FromObject(obj, jsonSerializer).ToDictionary(excludeFields);
            else
                return JObject.FromObject(obj).ToDictionary(excludeFields);
        }

        private static void ProcessJObjectProperties(IDictionary<string, object> propertyValuePairs, params string[] excludeFields)
        {
            var objectPropertyNames = (from property in propertyValuePairs
                                       let propertyName = property.Key
                                       let value = property.Value
                                       where value is JObject
                                       select propertyName)
                                       .Where(p => !excludeFields.Contains(p))
                                       .ToList();

            objectPropertyNames.ForEach(propertyName => propertyValuePairs[propertyName] = ToDictionary((JObject)propertyValuePairs[propertyName]));
        }

        private static void ProcessJArrayProperties(IDictionary<string, object> propertyValuePairs, params string[] excludeFields)
        {
            var arrayPropertyNames = (from property in propertyValuePairs
                                      let propertyName = property.Key
                                      let value = property.Value
                                      where value is JArray
                                      select propertyName)
                                      .Where(p => !excludeFields.Contains(p))
                                      .ToList();

            arrayPropertyNames.ForEach(propertyName => propertyValuePairs[propertyName] = ToArray((JArray)propertyValuePairs[propertyName]));
        }

        public static object[] ToArray(this JArray array)
        {
            return array.ToObject<object[]>().Select(ProcessArrayEntry).ToArray();
        }

        private static object ProcessArrayEntry(object value)
        {
            if (value is JObject)
            {
                return ToDictionary((JObject)value);
            }
            if (value is JArray)
            {
                return ToArray((JArray)value);
            }
            return value;
        }
    }
}