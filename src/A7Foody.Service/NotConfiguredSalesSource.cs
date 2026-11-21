using A7Foody.Core.Sales;

namespace A7Foody.Service;

/// <summary>
/// Placeholder até a Alpha7 enviar a documentação da "Base de Dados para Integrações Externas".
/// Não lê nada; só avisa no log. Será substituído pelo adaptador A7Foody.A7.
/// </summary>
internal sealed class NotConfiguredSalesSource(ILogger<NotConfiguredSalesSource> logger) : ISalesSource
{
    private bool _warned;

    public Task<SalesBatch> GetChangesAsync(string storeId, string? cursor, CancellationToken ct)
    {
        if (!_warned)
        {
            logger.LogWarning("Leitura do A7Pharma ainda não implementada: nenhuma venda será enviada. " +
                              "O acompanhamento de pedidos já existentes continua funcionando.");
            _warned = true;
        }

        return Task.FromResult(new SalesBatch([], cursor));
    }
}
