using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// Central JSON serializer for the save system. Uses Newtonsoft instead of
/// UnityEngine.JsonUtility because JsonUtility silently truncates object graphs
/// past ~7-10 levels of nesting, which corrupts deeply nested code-block trees
/// (the dropped data later throws KeyNotFoundException on load).
/// </summary>
public static class SaveSerializer
{
    static readonly JsonSerializerSettings settings = new()
    {
        // No depth cap: code-block nesting can go arbitrarily deep.
        MaxDepth = null,
        // Belt-and-suspenders against any accidental cycle (e.g. Unity struct
        // properties like Vector2.normalized); real Unity types use converters.
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        Converters = { new Vector2Converter() },
    };

    public static string Serialize(object value, bool pretty = false)
        => JsonConvert.SerializeObject(value, pretty ? Formatting.Indented : Formatting.None, settings);

    public static T Deserialize<T>(string json)
        => JsonConvert.DeserializeObject<T>(json, settings);
}

/// <summary>
/// Serializes Vector2 as {"x":..,"y":..}. Without this, Newtonsoft walks
/// Vector2's properties (normalized, magnitude, ...) which self-reference and
/// blow up serialization.
/// </summary>
public class Vector2Converter : JsonConverter<Vector2>
{
    public override void WriteJson(JsonWriter writer, Vector2 value, JsonSerializer serializer)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("x");
        writer.WriteValue(value.x);
        writer.WritePropertyName("y");
        writer.WriteValue(value.y);
        writer.WriteEndObject();
    }

    public override Vector2 ReadJson(JsonReader reader, System.Type objectType, Vector2 existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        float x = 0f, y = 0f;
        while (reader.Read() && reader.TokenType == JsonToken.PropertyName)
        {
            string prop = (string)reader.Value;
            reader.Read();
            if (prop == "x") x = System.Convert.ToSingle(reader.Value);
            else if (prop == "y") y = System.Convert.ToSingle(reader.Value);
        }
        return new Vector2(x, y);
    }
}
