using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
            public override void WriteJson(JsonWriter writer,object value,JsonSerializer serializer){
                if(value==null){writer.WriteNull();return;}
                var node=(SaveItemNode)value;
                writer.WriteStartObject();writer.WritePropertyName("native");writer.WriteValue(JsonUtility.ToJson(node));
                // Unity's JSON reader can round a double by one ULP. Carry the original bits for
                // numerical tags so chemistry and genetics are exact copies of the host's values.
                writer.WritePropertyName("bits");writer.WriteStartArray();
                if(node.itemState!=null)foreach(var p in node.itemState.dict){
                    var tag=p.Value;if(tag==null || (tag.valueDouble==0d && tag.valueFloat==0f))continue;
                    writer.WriteStartArray();writer.WriteValue(p.Key);writer.WriteValue(BitConverter.DoubleToInt64Bits(tag.valueDouble));
                    writer.WriteValue(BitConverter.ToInt32(BitConverter.GetBytes(tag.valueFloat),0));writer.WriteEndArray();
                }
                writer.WriteEndArray();writer.WriteEndObject();
            }
            public override object ReadJson(JsonReader reader,Type type,object existing,JsonSerializer serializer){
                if(reader.TokenType==JsonToken.Null)return null;
                if(reader.TokenType==JsonToken.String)return JsonUtility.FromJson<SaveItemNode>((string)reader.Value);
                if(reader.TokenType!=JsonToken.StartObject)throw new JsonSerializationException("Invalid item record");
                var obj=JObject.Load(reader);var node=JsonUtility.FromJson<SaveItemNode>((string)obj["native"]);
                var bits=obj["bits"] as JArray;if(node==null || bits==null)throw new JsonSerializationException("Invalid numeric record");
                foreach(var row in bits){var a=row as JArray;if(a==null || a.Count!=3)throw new JsonSerializationException("Invalid numeric tag");
                    TagState tag;if(!node.itemState.dict.TryGetValue((string)a[0],out tag))throw new JsonSerializationException("Missing numeric tag");
                    tag.valueDouble=BitConverter.Int64BitsToDouble((long)a[1]);tag.valueFloat=BitConverter.ToSingle(BitConverter.GetBytes((int)a[2]),0);
                }
                return node;
            }
        }
        sealed class FeatureConverter : JsonConverter {
            public override bool CanConvert(Type type){return type==typeof(ItemFeature);}
            public override void WriteJson(JsonWriter writer,object value,JsonSerializer serializer){writer.WriteValue(value==null?null:Convert.ToBase64String(ES3.Serialize((ItemFeature)value,new ES3Settings(false){prettyPrint=false})));}
            public override object ReadJson(JsonReader reader,Type type,object existing,JsonSerializer serializer){if(reader.TokenType==JsonToken.Null)return null;if(reader.TokenType!=JsonToken.String)throw new JsonSerializationException("Invalid item feature");return ES3.Deserialize<ItemFeature>(Convert.FromBase64String((string)reader.Value),new ES3Settings(false));}
        }
    }
}
