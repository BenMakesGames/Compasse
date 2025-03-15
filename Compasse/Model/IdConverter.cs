using System.Text.Json;
using System.Text.Json.Serialization;

namespace Compasse.Model;

public sealed class IdConverter: JsonConverter<string>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.TryGetInt64(out long longValue) ? longValue.ToString() : reader.GetDouble().ToString(),
            _ => throw new JsonException($"Cannot convert {reader.TokenType} to id. Only strings and numbers are supported.")
        };
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }
}
