using System.Data.Common;
using A7Foody.A7;
using A7Foody.Core.Sales;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace A7Foody.Tests;

/// <summary>Leitor do A7 contra um SQLite que imita o banco da Alpha7 (mesmo contrato de colunas).</summary>
public sealed class A7SqlSalesSourceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"a7src-{Guid.NewGuid():N}");
    private readonly SqliteConnection _keepAlive;
    private readonly string _cs;
    private readonly ManualClock _clock = new(new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.FromHours(-3)));
    private readonly A7SourceOptions _options;

    public A7SqlSalesSourceTests()
    {
        Directory.CreateDirectory(_dir);
        _cs = $"Data Source=a7-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(_cs);
        _keepAlive.Open();
        Exec("""
            CREATE TABLE vendas (id TEXT, loja TEXT, alterado TEXT, criado TEXT, cancelada INTEGER, total REAL, taxa REAL,
                                 pagto TEXT, cliente TEXT, fone TEXT, rua TEXT, num TEXT, bairro TEXT, cidade TEXT, uf TEXT, cep TEXT);
            CREATE TABLE itens (venda TEXT, descricao TEXT, qtd REAL, preco REAL);
            """);

        File.WriteAllText(Path.Combine(_dir, "vendas.sql"), """
            SELECT id AS venda_id, alterado AS alterado_em, criado AS criado_em, cancelada, total AS valor_total,
                   taxa AS taxa_entrega, pagto AS forma_pagamento, cliente AS cliente_nome, fone AS cliente_telefone,
                   rua AS end_logradouro, num AS end_numero, bairro AS end_bairro, cidade AS end_cidade, uf AS end_uf, cep AS end_cep
            FROM vendas
            WHERE loja = @loja
              AND (alterado > @desde OR (alterado = @desde AND id > @desde_id))
              AND alterado <= @ate
            ORDER BY alterado, id
            LIMIT @limite
            """);
        File.WriteAllText(Path.Combine(_dir, "itens.sql"),
            "SELECT descricao, qtd AS quantidade, preco AS valor_unitario FROM itens WHERE venda = @venda_id");

        _options = new A7SourceOptions
        {
            SalesQueryFile = Path.Combine(_dir, "vendas.sql"),
            ItemsQueryFile = Path.Combine(_dir, "itens.sql"),
            BatchSize = 2,
        };
    }

    public void Dispose()
    {
        _keepAlive.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    private void Exec(string sql)
    {
        using var cmd = _keepAlive.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private void Venda(string id, string alterado, bool cancelada = false, string loja = "1") => Exec($"""
        INSERT INTO vendas VALUES ('{id}', '{loja}', '2026-11-23 {alterado}', '2026-11-23 {alterado}', {(cancelada ? 1 : 0)},
                                   57.8, 5, 'PIX', 'Maria', '19998765432', 'Rua A', '10', 'Centro', 'Limeira', 'SP', '13480000');
        INSERT INTO itens VALUES ('{id}', 'Dipirona', 2, 12.9);
        """);

    private A7SqlSalesSource Source() =>
        new(new SqliteFactory(_cs), _options, _clock, NullLogger<A7SqlSalesSource>.Instance);

    private sealed class SqliteFactory(string cs) : IA7ConnectionFactory
    {
        public DbConnection Create(string storeId) => new SqliteConnection(cs);
    }

    [Fact]
    public async Task First_run_does_not_read_history()
    {
        Venda("100", "08:00:00"); // antes de o conector existir

        var batch = await Source().GetChangesAsync("1", null, CancellationToken.None);

        Assert.Empty(batch.Sales);
        Assert.NotNull(batch.NextCursor); // ponto de partida gravado

        Venda("101", "12:00:30"); // venda feita depois que o conector começou
        _clock.Advance(TimeSpan.FromMinutes(1));
        var next = await Source().GetChangesAsync("1", batch.NextCursor, CancellationToken.None);
        Assert.Equal("101", Assert.Single(next.Sales).SaleId);
    }

    [Fact]
    public async Task Pages_through_sales_with_same_timestamp_without_skipping()
    {
        var start = await Source().GetChangesAsync("1", null, CancellationToken.None);
        Venda("201", "12:00:00");
        Venda("202", "12:00:00");
        Venda("203", "12:00:00");
        _clock.Advance(TimeSpan.FromMinutes(1));

        var p1 = await Source().GetChangesAsync("1", start.NextCursor, CancellationToken.None);
        var p2 = await Source().GetChangesAsync("1", p1.NextCursor, CancellationToken.None);
        var p3 = await Source().GetChangesAsync("1", p2.NextCursor, CancellationToken.None);

        Assert.Equal(["201", "202"], p1.Sales.Select(s => s.SaleId));
        Assert.Equal(["203"], p2.Sales.Select(s => s.SaleId));
        Assert.Empty(p3.Sales);
        Assert.Equal(p2.NextCursor, p3.NextCursor);
    }

    [Fact]
    public async Task Respects_commit_lag()
    {
        var start = await Source().GetChangesAsync("1", null, CancellationToken.None);
        Venda("301", "12:00:05"); // alterada há 5 s, abaixo do CommitLag de 10 s
        _clock.Advance(TimeSpan.FromSeconds(10));

        Assert.Empty((await Source().GetChangesAsync("1", start.NextCursor, CancellationToken.None)).Sales);

        _clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Single((await Source().GetChangesAsync("1", start.NextCursor, CancellationToken.None)).Sales);
    }

    [Fact]
    public async Task Maps_columns_items_and_cancellation()
    {
        var start = await Source().GetChangesAsync("1", null, CancellationToken.None);
        Venda("401", "12:00:00");
        Venda("402", "12:00:01", cancelada: true);
        Venda("403", "12:00:02", loja: "2");
        _clock.Advance(TimeSpan.FromMinutes(1));

        var sales = (await Source().GetChangesAsync("1", start.NextCursor, CancellationToken.None)).Sales;

        Assert.Equal(2, sales.Count); // loja 2 filtrada
        var s = sales[0];
        Assert.Equal(new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.FromHours(-3)), s.CreatedAt);
        Assert.Equal(57.8m, s.Total);
        Assert.Equal("PIX", s.PaymentCode);
        Assert.Equal("Rua A", s.Address.Street);
        Assert.Equal("13480000", s.Address.PostalCode);
        Assert.Equal(new SaleItem("Dipirona", 2, 12.9m), Assert.Single(s.Items));
        Assert.Equal(SaleState.Cancelled, sales[1].State);
        Assert.Empty(sales[1].Items);
    }

    [Fact]
    public async Task Missing_required_column_gives_clear_error()
    {
        File.WriteAllText(_options.SalesQueryFile, "SELECT id AS venda_id FROM vendas");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Source().GetChangesAsync("1", null, CancellationToken.None));
        Assert.Contains("alterado_em", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Cursor_roundtrip()
    {
        var c = new SqlCursor(new DateTime(2026, 11, 23, 12, 0, 0, 123), "A|B");
        Assert.Equal(c, SqlCursor.Decode(c.Encode()));
        Assert.Null(SqlCursor.Decode("lixo"));
    }
}
