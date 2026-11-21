using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace A7Foody.Foody.Json;

/// <summary>
/// Converte enums usando <see cref="JsonStringEnumMemberName"/> e, ao ler, devolve o valor 0
/// (Unknown) para strings desconhecidas em vez de lançar exceção. Isso evita que um status novo
/// criado pela Foody derrube a sincronização inteira.
/// </summary>
internal sealed class FoodyEnumConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    private static readonly FrozenDictionary<string, TEnum> FromJson;
    private static readonly FrozenDictionary<TEnum, string> ToJson;

#pragma warning disable CA1810 // inicialização estática explícita é mais clara aqui
    static FoodyEnumConverter()
#pragma warning restore CA1810
    {
        var from = new Dictionary<string, TEnum>(StringComparer.OrdinalIgnoreCase);
        var to = new Dictionary<TEnum, string>();
        foreach (var field in typeof(TEnum).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var name = field.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name;
            if (name is null)
            {
                continue;
            }

            var value = (TEnum)field.GetValue(null)!;
            from[name] = value;
            to[value] = name;
        }

        FromJson = from.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        ToJson = to.ToFrozenDictionary();
    }

    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String && reader.GetString() is { } s && FromJson.TryGetValue(s, out var v))
        {
            return v;
        }

        return default;
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        if (!ToJson.TryGetValue(value, out var name))
        {
            throw new JsonException($"Valor {value} de {typeof(TEnum).Name} não pode ser enviado à Foody.");
        }

        writer.WriteStringValue(name);
    }
}
