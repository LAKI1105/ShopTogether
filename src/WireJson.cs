using System;
using Newtonsoft.Json;
using UnityEngine;

namespace ShopTogether {
    internal static class WireJson {
        static readonly JsonSerializerSettings settings=new JsonSerializerSettings{
            TypeNameHandling=TypeNameHandling.None,MetadataPropertyHandling=MetadataPropertyHandling.Ignore,
            MaxDepth=64,ObjectCreationHandling=ObjectCreationHandling.Replace,
            Converters=new JsonConverter[]{new NativeNodeConverter(),new FeatureConverter()}
        };
        internal static string ToJson(object value){return JsonConvert.SerializeObject(value,Formatting.None,settings);}
        internal static T FromJson<T>(string value){if(value==null||value.Length>LargeTransfer.MaxPayload)throw new JsonSerializationException("Invalid packet size");return JsonConvert.DeserializeObject<T>(value,settings);}
        // Native game records retain the game's own serialization callbacks. Wire DTOs use a serializer
        // that supports classes in dynamically loaded mod assemblies; no remote CLR type names are used.
        sealed class NativeNodeConverter : JsonConverter {
            public override bool CanConvert(Type type){return type==typeof(SaveItemNode);}
            public override void WriteJson(JsonWriter writer,object value,JsonSerializer serializer){writer.WriteValue(value==null?null:JsonUtility.ToJson(value));}
            public override object ReadJson(JsonReader reader,Type type,object existing,JsonSerializer serializer){if(reader.TokenType==JsonToken.Null)return null;if(reader.TokenType!=JsonToken.String)throw new JsonSerializationException("Invalid item record");return JsonUtility.FromJson<SaveItemNode>((string)reader.Value);}
        }
        sealed class FeatureConverter : JsonConverter {
            public override bool CanConvert(Type type){return type==typeof(ItemFeature);}
            public override void WriteJson(JsonWriter writer,object value,JsonSerializer serializer){writer.WriteValue(value==null?null:Convert.ToBase64String(ES3.Serialize((ItemFeature)value,new ES3Settings(false){prettyPrint=false})));}
            public override object ReadJson(JsonReader reader,Type type,object existing,JsonSerializer serializer){if(reader.TokenType==JsonToken.Null)return null;if(reader.TokenType!=JsonToken.String)throw new JsonSerializationException("Invalid item feature");return ES3.Deserialize<ItemFeature>(Convert.FromBase64String((string)reader.Value),new ES3Settings(false));}
        }
    }
}
