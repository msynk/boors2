namespace Boors.Market.Sources.Json;

/// <summary>
/// Reads a decimal that a source sends as a number, a numeric string, or a placeholder such as "-" (→ null).
/// TSETMC does all three for the same field depending on the row (e.g. market-watch <c>pe</c>).
/// </summary>
public sealed class FlexibleDecimalConverter : JsonConverter<decimal?>
{
    public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.TryGetDecimal(out var number) ? number : null;
            case JsonTokenType.String:
                var text = reader.GetString();
                return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
            case JsonTokenType.Null:
                return null;
            default:
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options)
    {
        if (value is { } v)
            writer.WriteNumberValue(v);
        else
            writer.WriteNullValue();
    }
}
