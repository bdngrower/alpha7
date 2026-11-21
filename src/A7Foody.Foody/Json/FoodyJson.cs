using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace A7Foody.Foody.Json;

/// <summary>Configuração JSON compartilhada com a API da Foody.</summary>
public static class FoodyJson
{
    /// <summary>Opções usadas em todas as chamadas (camelCase, ignora nulos, datas no formato da Foody).</summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        };
        o.Converters.Add(new FoodyDateTimeOffsetConverter());
        o.MakeReadOnly();
        return o;
    }
}

/// <summary>
/// A Foody exige datas com fuso no formato <c>yyyy-MM-dd'T'HH:mm:ssXXX</c>, sem frações de segundo
/// (ex.: <c>2020-06-22T21:30:01-03:00</c>). Na leitura aceita qualquer ISO 8601, inclusive UTC com "Z",
/// que aparece nos webhooks.
/// </summary>
internal sealed class FoodyDateTimeOffsetConverter : JsonConverter<DateTimeOffset>
{
    internal const string Format = "yyyy-MM-dd'T'HH:mm:sszzz";

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        DateTimeOffset.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString(Format, CultureInfo.InvariantCulture));
}
