using System.Text.Json;
using System.Text.Json.Serialization;

namespace CanfarDesktop.Mcp.Wire;

/// <summary>
/// Type-erased arbitrary JSON value (7 cases), 1-to-1 with the macOS <c>JSONValue</c>. Used for the
/// open-ended parts of the MCP wire protocol — tool input schemas, <c>tools/call</c> arguments, and
/// JSON-RPC error <c>data</c>. Round-trips losslessly via <see cref="JsonValueConverter"/>.
/// </summary>
[JsonConverter(typeof(JsonValueConverter))]
public abstract record JsonValue
{
    public static readonly JsonValue Null = new JsonNull();

    public static JsonValue Of(bool value) => new JsonBool(value);
    public static JsonValue Of(long value) => new JsonInt(value);
    public static JsonValue Of(double value) => new JsonDouble(value);
    public static JsonValue Of(string value) => new JsonString(value);
    public static JsonValue Of(IReadOnlyList<JsonValue> items) => new JsonArray(items);
    public static JsonValue Of(IReadOnlyDictionary<string, JsonValue> members) => new JsonObject(members);

    /// <summary>Parse a JSON document into a <see cref="JsonValue"/> tree. Throws on malformed JSON.</summary>
    public static JsonValue Parse(string json)
        => JsonSerializer.Deserialize<JsonValue>(json) ?? Null;

    /// <summary>
    /// Parse a UTF-8 JSON document from raw bytes, tolerating a leading byte-order mark. Used at the
    /// transport boundary where a client may newline-frame a message that carries a BOM (RFC 8259
    /// lets parsers ignore one). Throws on malformed JSON.
    /// </summary>
    public static JsonValue Parse(ReadOnlySpan<byte> utf8)
    {
        if (utf8.Length >= 3 && utf8[0] == 0xEF && utf8[1] == 0xBB && utf8[2] == 0xBF)
            utf8 = utf8[3..];
        return JsonSerializer.Deserialize<JsonValue>(utf8) ?? Null;
    }

    /// <summary>Serialize this value back to JSON.</summary>
    public string ToJsonString() => JsonSerializer.Serialize(this);

    /// <summary>Look up a member of a JSON object, or null when this isn't an object / key is absent.</summary>
    public JsonValue? this[string key]
        => this is JsonObject o && o.Members.TryGetValue(key, out var v) ? v : null;
}

public sealed record JsonNull : JsonValue;
public sealed record JsonBool(bool Value) : JsonValue;
public sealed record JsonInt(long Value) : JsonValue;
public sealed record JsonDouble(double Value) : JsonValue;
public sealed record JsonString(string Value) : JsonValue;
public sealed record JsonArray(IReadOnlyList<JsonValue> Items) : JsonValue;
public sealed record JsonObject(IReadOnlyDictionary<string, JsonValue> Members) : JsonValue;

/// <summary>
/// <see cref="JsonValue"/> ↔ JSON converter. Decodes numbers as <see cref="JsonInt"/> when they fit
/// in a 64-bit integer, else <see cref="JsonDouble"/> (mirrors the macOS decode order).
/// </summary>
public sealed class JsonValueConverter : JsonConverter<JsonValue>
{
    public override JsonValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return JsonValue.Null;
            case JsonTokenType.True:
                return new JsonBool(true);
            case JsonTokenType.False:
                return new JsonBool(false);
            case JsonTokenType.Number:
                return reader.TryGetInt64(out var l) ? new JsonInt(l) : new JsonDouble(reader.GetDouble());
            case JsonTokenType.String:
                return new JsonString(reader.GetString()!);
            case JsonTokenType.StartArray:
            {
                var items = new List<JsonValue>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    items.Add(Read(ref reader, typeToConvert, options));
                return new JsonArray(items);
            }
            case JsonTokenType.StartObject:
            {
                var members = new Dictionary<string, JsonValue>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    var name = reader.GetString()!;
                    reader.Read();
                    members[name] = Read(ref reader, typeToConvert, options);
                }
                return new JsonObject(members);
            }
            default:
                throw new JsonException($"Unexpected token {reader.TokenType} decoding JsonValue");
        }
    }

    public override void Write(Utf8JsonWriter writer, JsonValue value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case JsonNull:
                writer.WriteNullValue();
                break;
            case JsonBool b:
                writer.WriteBooleanValue(b.Value);
                break;
            case JsonInt i:
                writer.WriteNumberValue(i.Value);
                break;
            case JsonDouble d:
                writer.WriteNumberValue(d.Value);
                break;
            case JsonString s:
                writer.WriteStringValue(s.Value);
                break;
            case JsonArray a:
                writer.WriteStartArray();
                foreach (var item in a.Items) Write(writer, item, options);
                writer.WriteEndArray();
                break;
            case JsonObject o:
                writer.WriteStartObject();
                foreach (var (k, v) in o.Members)
                {
                    writer.WritePropertyName(k);
                    Write(writer, v, options);
                }
                writer.WriteEndObject();
                break;
            default:
                throw new JsonException($"Unknown JsonValue subtype {value.GetType().Name}");
        }
    }
}
