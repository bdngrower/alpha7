namespace A7Foody.Core.Sync;

/// <summary>Persistência do conector (implementada em A7Foody.Storage com SQLite).</summary>
public interface IOrderStore
{
    /// <summary>Cria as tabelas, se necessário.</summary>
    Task InitializeAsync(CancellationToken ct);

    /// <summary>Busca uma venda rastreada.</summary>
    Task<TrackedOrder?> GetAsync(string storeId, string saleId, CancellationToken ct);

    /// <summary>Insere ou atualiza.</summary>
    Task SaveAsync(TrackedOrder order, CancellationToken ct);

    /// <summary>Registros nas etapas informadas cujo <see cref="TrackedOrder.NextAttemptAt"/> já passou.</summary>
    Task<IReadOnlyList<TrackedOrder>> GetDueAsync(string storeId, IReadOnlyCollection<SyncState> states, DateTimeOffset now, int limit, CancellationToken ct);

    /// <summary>Lê o cursor da fonte de vendas da loja.</summary>
    Task<string?> GetCursorAsync(string storeId, CancellationToken ct);

    /// <summary>Grava o cursor da fonte de vendas da loja.</summary>
    Task SetCursorAsync(string storeId, string? cursor, CancellationToken ct);
}
