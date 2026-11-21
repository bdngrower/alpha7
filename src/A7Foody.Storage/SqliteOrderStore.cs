using System.Globalization;
using A7Foody.Core.Sync;
using A7Foody.Foody.Models;
using Dapper;
using Microsoft.Data.Sqlite;

namespace A7Foody.Storage;

/// <summary>
/// Banco local do conector em SQLite (um arquivo). Guarda o vínculo venda A7 ↔ pedido Foody, a outbox e
/// os cursores. Não escreve nada no banco da Alpha7.
/// </summary>
public sealed class SqliteOrderStore : IOrderStore
{
    private readonly string _connectionString;

    /// <summary>Cria o store apontando para o arquivo informado.</summary>
    public SqliteOrderStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var dir = Path.GetDirectoryName(Path.GetFullPath(databasePath));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            DefaultTimeout = 30,
        }.ToString();
    }

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await c.ExecuteAsync(new CommandDefinition(Schema.Sql, cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TrackedOrder?> GetAsync(string storeId, string saleId, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        var row = await c.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            "SELECT * FROM orders WHERE store_id = @storeId AND sale_id = @saleId",
            new { storeId, saleId }, cancellationToken: ct)).ConfigureAwait(false);
        return row?.ToModel();
    }

    /// <inheritdoc />
    public async Task SaveAsync(TrackedOrder order, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(order);
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await c.ExecuteAsync(new CommandDefinition("""
            INSERT INTO orders (store_id, sale_id, foody_order_id, foody_uid, state, foody_status, sale_created_at,
                                payload_json, attempts, next_attempt_at, last_error, cancel_requested, created_at, updated_at)
            VALUES (@StoreId, @SaleId, @FoodyOrderId, @FoodyUid, @State, @FoodyStatus, @SaleCreatedAt,
                    @PayloadJson, @Attempts, @NextAttemptAt, @LastError, @CancelRequested, @CreatedAt, @UpdatedAt)
            ON CONFLICT (store_id, sale_id) DO UPDATE SET
                foody_order_id = excluded.foody_order_id, foody_uid = excluded.foody_uid, state = excluded.state,
                foody_status = excluded.foody_status, sale_created_at = excluded.sale_created_at,
                payload_json = excluded.payload_json, attempts = excluded.attempts,
                next_attempt_at = excluded.next_attempt_at, last_error = excluded.last_error,
                cancel_requested = excluded.cancel_requested, updated_at = excluded.updated_at
            """, Row.FromModel(order), cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TrackedOrder>> GetDueAsync(string storeId, IReadOnlyCollection<SyncState> states,
        DateTimeOffset now, int limit, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        var rows = await c.QueryAsync<Row>(new CommandDefinition("""
            SELECT * FROM orders
            WHERE store_id = @storeId AND state IN @states AND next_attempt_at <= @now
            ORDER BY next_attempt_at
            LIMIT @limit
            """,
            new { storeId, states = states.Select(s => (int)s).ToArray(), now = Ticks(now), limit },
            cancellationToken: ct)).ConfigureAwait(false);
        return [.. rows.Select(r => r.ToModel())];
    }

    /// <inheritdoc />
    public async Task<string?> GetCursorAsync(string storeId, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        return await c.QuerySingleOrDefaultAsync<string?>(new CommandDefinition(
            "SELECT cursor FROM source_cursors WHERE store_id = @storeId", new { storeId }, cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetCursorAsync(string storeId, string? cursor, CancellationToken ct)
    {
        await using var c = await OpenAsync(ct).ConfigureAwait(false);
        await c.ExecuteAsync(new CommandDefinition("""
            INSERT INTO source_cursors (store_id, cursor, updated_at) VALUES (@storeId, @cursor, @now)
            ON CONFLICT (store_id) DO UPDATE SET cursor = excluded.cursor, updated_at = excluded.updated_at
            """, new { storeId, cursor, now = Ticks(DateTimeOffset.UtcNow) }, cancellationToken: ct)).ConfigureAwait(false);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var c = new SqliteConnection(_connectionString);
        await c.OpenAsync(ct).ConfigureAwait(false);
        return c;
    }

    // Datas gravadas como ticks UTC (comparáveis e sem ambiguidade de fuso) + offset original quando importa.
    private static long Ticks(DateTimeOffset d) => d.UtcTicks;

    private static DateTimeOffset FromTicks(long ticks) => new(ticks, TimeSpan.Zero);

#pragma warning disable CA1812, IDE1006 // instanciada pelo Dapper; nomes iguais às colunas
    private sealed class Row
    {
        public string store_id { get; set; } = "";
        public string sale_id { get; set; } = "";
        public string? foody_order_id { get; set; }
        public string? foody_uid { get; set; }
        public long state { get; set; }
        public long foody_status { get; set; }
        public string sale_created_at { get; set; } = "";
        public string? payload_json { get; set; }
        public long attempts { get; set; }
        public long next_attempt_at { get; set; }
        public string? last_error { get; set; }
        public long cancel_requested { get; set; }
        public long created_at { get; set; }
        public long updated_at { get; set; }

        public TrackedOrder ToModel() => new()
        {
            StoreId = store_id,
            SaleId = sale_id,
            FoodyOrderId = foody_order_id,
            FoodyUid = foody_uid,
            State = (SyncState)state,
            FoodyStatus = (FoodyOrderStatus)foody_status,
            SaleCreatedAt = DateTimeOffset.Parse(sale_created_at, CultureInfo.InvariantCulture),
            PayloadJson = payload_json,
            Attempts = (int)attempts,
            NextAttemptAt = FromTicks(next_attempt_at),
            LastError = last_error,
            CancelRequested = cancel_requested != 0,
            CreatedAt = FromTicks(created_at),
            UpdatedAt = FromTicks(updated_at),
        };

        public static object FromModel(TrackedOrder o) => new
        {
            o.StoreId,
            o.SaleId,
            o.FoodyOrderId,
            o.FoodyUid,
            State = (int)o.State,
            FoodyStatus = (int)o.FoodyStatus,
            SaleCreatedAt = o.SaleCreatedAt.ToString("O", CultureInfo.InvariantCulture),
            o.PayloadJson,
            o.Attempts,
            NextAttemptAt = Ticks(o.NextAttemptAt),
            o.LastError,
            CancelRequested = o.CancelRequested ? 1 : 0,
            CreatedAt = Ticks(o.CreatedAt),
            UpdatedAt = Ticks(o.UpdatedAt),
        };
    }
#pragma warning restore CA1812, IDE1006
}
