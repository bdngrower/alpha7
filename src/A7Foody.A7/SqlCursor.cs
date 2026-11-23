using System.Globalization;

namespace A7Foody.A7;

/// <summary>
/// Posição de leitura: última (data de alteração, id de venda) processada. Leitura por "keyset"
/// (<c>(alterado_em, venda_id) &gt; (@desde, @desde_id)</c>), que não pula nem repete registros com a mesma data.
/// A data é a do relógio do banco (sem fuso), exatamente como está na coluna.
/// </summary>
internal readonly record struct SqlCursor(DateTime ChangedAt, string SaleId)
{
    private const string Prefix = "v1|";

    public string Encode() =>
        string.Create(CultureInfo.InvariantCulture, $"{Prefix}{ChangedAt.Ticks}|{SaleId}");

    public static SqlCursor? Decode(string? value)
    {
        if (string.IsNullOrEmpty(value) || !value.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var parts = value[Prefix.Length..].Split('|', 2);
        return parts.Length == 2 && long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            ? new SqlCursor(new DateTime(ticks, DateTimeKind.Unspecified), parts[1])
            : null;
    }
}
