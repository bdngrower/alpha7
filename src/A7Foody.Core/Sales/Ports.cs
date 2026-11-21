using A7Foody.Foody.Models;

namespace A7Foody.Core.Sales;

/// <summary>
/// Fonte das vendas com entrega (implementada pelo adaptador do A7Pharma).
/// </summary>
public interface ISalesSource
{
    /// <summary>
    /// Devolve vendas com entrega criadas ou alteradas (inclusive canceladas) desde <paramref name="cursor"/>.
    /// O cursor é opaco para o motor: o adaptador escolhe o que usar (data de alteração, rowversion, ID
    /// sequencial...). <c>null</c> = primeira execução. Deve ser seguro devolver a mesma venda mais de uma vez.
    /// </summary>
    Task<SalesBatch> GetChangesAsync(string storeId, string? cursor, CancellationToken ct);
}

/// <summary>Lote de vendas e o cursor para a próxima leitura.</summary>
public sealed record SalesBatch(IReadOnlyList<DeliverySale> Sales, string? NextCursor);

/// <summary>
/// Destino das mudanças de status da entrega (ex.: gravar de volta no A7Pharma, se a Alpha7 permitir).
/// </summary>
public interface IDeliveryStatusSink
{
    /// <summary>Chamado quando o status de uma entrega muda na Foody.</summary>
    Task OnStatusChangedAsync(DeliveryStatusChange change, CancellationToken ct);
}

/// <summary>Mudança de status de uma entrega.</summary>
public sealed record DeliveryStatusChange(
    string StoreId,
    string SaleId,
    string FoodyUid,
    FoodyOrderStatus Previous,
    FoodyOrderStatus Current,
    DateTimeOffset? ChangedAt);

/// <summary>Sink padrão: não grava nada (o acompanhamento fica no banco do conector e no portal Foody).</summary>
public sealed class NullDeliveryStatusSink : IDeliveryStatusSink
{
    /// <inheritdoc />
    public Task OnStatusChangedAsync(DeliveryStatusChange change, CancellationToken ct) => Task.CompletedTask;
}
