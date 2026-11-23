using System.Data.Common;
using A7Foody.Core.Sales;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace A7Foody.A7;

/// <summary>Abre conexões com o banco do A7 de uma loja.</summary>
public interface IA7ConnectionFactory
{
    /// <summary>Nova conexão (fechada) para a loja.</summary>
    DbConnection Create(string storeId);
}

/// <summary>Conexões PostgreSQL a partir de <see cref="A7SourceOptions.ConnectionStrings"/>.</summary>
public sealed class NpgsqlA7ConnectionFactory(A7SourceOptions options) : IA7ConnectionFactory
{
    /// <inheritdoc />
    public DbConnection Create(string storeId) =>
        options.ConnectionStrings.TryGetValue(storeId, out var cs) && !string.IsNullOrWhiteSpace(cs)
            ? new NpgsqlConnection(cs)
            : throw new InvalidOperationException($"Sem A7:ConnectionStrings para a loja {storeId}.");
}

/// <summary>
/// Lê as vendas com entrega por consultas SQL em arquivo. O "onde estão os dados no A7" fica todo nos
/// arquivos .sql — se a Alpha7 criar uma tabela nova, mudar o banco ou liberarmos leitura direta, só os
/// arquivos mudam. O contrato das colunas está em <c>sql/LEIAME.md</c>.
/// </summary>
public sealed class A7SqlSalesSource : ISalesSource
{
    private static readonly string[] SalesRequired = ["venda_id", "alterado_em", "criado_em"];
    private static readonly string[] ItemsRequired = ["descricao", "quantidade", "valor_unitario"];

    private readonly IA7ConnectionFactory _connections;
    private readonly A7SourceOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<A7SqlSalesSource> _logger;
    private readonly TimeZoneInfo _dbZone;
    private readonly Lazy<string> _salesSql;
    private readonly Lazy<string> _itemsSql;

    /// <summary>Cria a fonte.</summary>
    public A7SqlSalesSource(IA7ConnectionFactory connections, A7SourceOptions options, TimeProvider clock, ILogger<A7SqlSalesSource> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _connections = connections;
        _options = options;
        _clock = clock;
        _logger = logger;
        _dbZone = TimeZoneInfo.FindSystemTimeZoneById(options.DatabaseTimeZone);
        _salesSql = new Lazy<string>(() => LoadSql(options.SalesQueryFile));
        _itemsSql = new Lazy<string>(() => LoadSql(options.ItemsQueryFile));
    }

    /// <inheritdoc />
    public async Task<SalesBatch> GetChangesAsync(string storeId, string? cursor, CancellationToken ct)
    {
        var dbNow = TimeZoneInfo.ConvertTime(_clock.GetUtcNow(), _dbZone).DateTime;
        var start = SqlCursor.Decode(cursor) ?? new SqlCursor(dbNow - _options.FirstRunLookback, "");
        var until = dbNow - _options.CommitLag;

        var connection = _connections.Create(storeId);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);

            var headers = await ReadSalesAsync(connection, storeId, start, until, ct).ConfigureAwait(false);
            var sales = new List<DeliverySale>(headers.Count);
            foreach (var (sale, _) in headers)
            {
                var items = sale.State == SaleState.Cancelled
                    ? []
                    : await ReadItemsAsync(connection, storeId, sale.SaleId, ct).ConfigureAwait(false);
                sales.Add(sale with { Items = items });
            }

            // Sem vendas novas: grava o ponto de partida mesmo assim, para a próxima leitura não recalcular
            // "agora" e pular vendas feitas entre um ciclo e outro.
            var next = headers.Count > 0 ? headers[^1].Cursor : start;
            if (headers.Count > 0)
            {
                _logger.LogInformation("Loja {Store}: {Count} venda(s) com entrega lidas do A7", storeId, headers.Count);
            }

            return new SalesBatch(sales, next.Encode());
        }
    }

    private async Task<List<(DeliverySale Sale, SqlCursor Cursor)>> ReadSalesAsync(
        DbConnection connection, string storeId, SqlCursor start, DateTime until, CancellationToken ct)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = _salesSql.Value;
        AddParameter(cmd, "loja", storeId);
        AddParameter(cmd, "desde", start.ChangedAt);
        AddParameter(cmd, "desde_id", start.SaleId);
        AddParameter(cmd, "ate", until);
        AddParameter(cmd, "limite", _options.BatchSize);

        var result = new List<(DeliverySale, SqlCursor)>();
        var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await using (reader.ConfigureAwait(false))
        {
            var row = new RowReader(reader, _options.SalesQueryFile);
            row.RequireColumns(SalesRequired);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var saleId = row.RequiredString("venda_id");
                var changedAt = row.DateTime("alterado_em")!.Value;
                result.Add((MapSale(row, storeId, saleId), new SqlCursor(changedAt, saleId)));
            }
        }

        return result;
    }

    private DeliverySale MapSale(RowReader r, string storeId, string saleId) => new()
    {
        StoreId = storeId,
        SaleId = saleId,
        CreatedAt = ToOffset(r.DateTime("criado_em")!.Value),
        DueAt = r.DateTime("prazo_entrega") is { } due ? ToOffset(due) : null,
        State = r.Bool("cancelada") ? SaleState.Cancelled : SaleState.Active,
        Total = r.Decimal("valor_total") ?? 0m,
        DeliveryFee = r.Decimal("taxa_entrega"),
        PaymentCode = r.String("forma_pagamento"),
        ChangeFor = r.Decimal("troco_para"),
        Notes = r.String("observacao"),
        IfoodLocalizer = r.String("ifood_localizador"),
        Customer = new SaleCustomer(r.String("cliente_nome"), r.String("cliente_telefone"), r.String("cliente_email")),
        Address = new SaleAddress
        {
            Street = r.String("end_logradouro"),
            Number = r.String("end_numero"),
            District = r.String("end_bairro"),
            City = r.String("end_cidade"),
            State = r.String("end_uf"),
            PostalCode = r.String("end_cep"),
            Complement = r.String("end_complemento"),
            Reference = r.String("end_referencia"),
            Latitude = r.Double("end_latitude"),
            Longitude = r.Double("end_longitude"),
        },
    };

    private async Task<IReadOnlyList<SaleItem>> ReadItemsAsync(DbConnection connection, string storeId, string saleId, CancellationToken ct)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = _itemsSql.Value;
        AddParameter(cmd, "loja", storeId);
        AddParameter(cmd, "venda_id", saleId);

        var items = new List<SaleItem>();
        var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await using (reader.ConfigureAwait(false))
        {
            var row = new RowReader(reader, _options.ItemsQueryFile);
            row.RequireColumns(ItemsRequired);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                items.Add(new SaleItem(row.RequiredString("descricao"), row.Decimal("quantidade") ?? 0m, row.Decimal("valor_unitario") ?? 0m));
            }
        }

        return items;
    }

    private DateTimeOffset ToOffset(DateTime dbLocal) => new(dbLocal, _dbZone.GetUtcOffset(dbLocal));

    private static void AddParameter(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    private static string LoadSql(string path)
    {
        var full = Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
        return File.Exists(full)
            ? File.ReadAllText(full)
            : throw new FileNotFoundException($"Arquivo de consulta do A7 não encontrado: {full}", full);
    }
}
