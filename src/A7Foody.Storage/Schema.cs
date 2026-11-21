namespace A7Foody.Storage;

/// <summary>
/// Esquema do banco local. Mudanças futuras: acrescentar novos blocos idempotentes e incrementar
/// <c>user_version</c>; nunca editar um bloco já publicado.
/// </summary>
internal static class Schema
{
    public const string Sql = """
        PRAGMA journal_mode = WAL;

        CREATE TABLE IF NOT EXISTS orders (
            store_id         TEXT    NOT NULL,
            sale_id          TEXT    NOT NULL,
            foody_order_id   TEXT,
            foody_uid        TEXT,
            state            INTEGER NOT NULL,   -- SyncState
            foody_status     INTEGER NOT NULL,   -- FoodyOrderStatus
            sale_created_at  TEXT    NOT NULL,   -- ISO 8601 com fuso
            payload_json     TEXT,
            attempts         INTEGER NOT NULL DEFAULT 0,
            next_attempt_at  INTEGER NOT NULL,   -- ticks UTC
            last_error       TEXT,
            cancel_requested INTEGER NOT NULL DEFAULT 0,
            created_at       INTEGER NOT NULL,   -- ticks UTC
            updated_at       INTEGER NOT NULL,   -- ticks UTC
            PRIMARY KEY (store_id, sale_id)
        );

        CREATE INDEX IF NOT EXISTS ix_orders_due ON orders (store_id, state, next_attempt_at);
        CREATE UNIQUE INDEX IF NOT EXISTS ux_orders_uid ON orders (foody_uid) WHERE foody_uid IS NOT NULL;

        CREATE TABLE IF NOT EXISTS source_cursors (
            store_id   TEXT    PRIMARY KEY,
            cursor     TEXT,
            updated_at INTEGER NOT NULL
        );

        PRAGMA user_version = 1;
        """;
}
