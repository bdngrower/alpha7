using System.Data.Common;
using System.Globalization;

namespace A7Foody.A7;

/// <summary>
/// Lê colunas pelo nome, sem diferenciar maiúsculas, convertendo tipos de forma tolerante (o mesmo código
/// funciona com PostgreSQL, MySQL ou SQLite nos testes). Coluna obrigatória ausente gera erro claro,
/// que aponta para o arquivo SQL a corrigir.
/// </summary>
internal sealed class RowReader
{
    private readonly DbDataReader _reader;
    private readonly Dictionary<string, int> _ordinals;
    private readonly string _source;

    public RowReader(DbDataReader reader, string source)
    {
        _reader = reader;
        _source = source;
        _ordinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < reader.FieldCount; i++)
        {
            _ordinals.TryAdd(reader.GetName(i), i);
        }
    }

    public void RequireColumns(params string[] names)
    {
        var missing = names.Where(n => !_ordinals.ContainsKey(n)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                $"A consulta {_source} não devolve as colunas obrigatórias: {string.Join(", ", missing)}. Veja sql/LEIAME.md.");
        }
    }

    private object? Raw(string name) =>
        _ordinals.TryGetValue(name, out var i) && !_reader.IsDBNull(i) ? _reader.GetValue(i) : null;

    public string? String(string name) => Raw(name) switch
    {
        null => null,
        string s => string.IsNullOrWhiteSpace(s) ? null : s.Trim(),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        var o => o.ToString(),
    };

    public string RequiredString(string name) =>
        String(name) ?? throw new InvalidOperationException($"Coluna {name} veio vazia em {_source}.");

    public decimal? Decimal(string name) => Raw(name) switch
    {
        null => null,
        string s => decimal.Parse(s, NumberStyles.Number, CultureInfo.InvariantCulture),
        var o => Convert.ToDecimal(o, CultureInfo.InvariantCulture),
    };

    public double? Double(string name) => Raw(name) switch
    {
        null => null,
        string s => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture),
        var o => Convert.ToDouble(o, CultureInfo.InvariantCulture),
    };

    public bool Bool(string name) => Raw(name) switch
    {
        null => false,
        bool b => b,
        string s => s.Trim().ToUpperInvariant() is "S" or "Y" or "T" or "TRUE" or "1" or "SIM",
        var o => Convert.ToInt64(o, CultureInfo.InvariantCulture) != 0,
    };

    /// <summary>Data como está no banco (sem fuso).</summary>
    public DateTime? DateTime(string name) => Raw(name) switch
    {
        null => null,
        System.DateTime d => System.DateTime.SpecifyKind(d, DateTimeKind.Unspecified),
        DateTimeOffset o => o.DateTime,
        string s => System.DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.None),
        var o => throw new InvalidOperationException($"Coluna {name} em {_source} não é data: {o.GetType().Name}."),
    };
}
