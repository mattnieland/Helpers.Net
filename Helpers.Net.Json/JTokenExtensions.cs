using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;

namespace Helpers.Net.Json
{
    public static class JTokenExtensions
    {
        public static bool IsNull(this JToken source)
        {
            return source == null || source.Type == JTokenType.Null;
        }

        public static bool IsArray(this JToken source)
        {
            return source.Type == JTokenType.Array;
        }

        public static JObject GetObject(this JToken source)
        {
            if(source.Type == JTokenType.Object)
                return (JObject)source;
            return null;
        }
    }
}
