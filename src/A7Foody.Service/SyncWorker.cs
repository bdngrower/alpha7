using A7Foody.Core.Sync;
using Microsoft.Extensions.Options;

namespace A7Foody.Service;

/// <summary>Executa o ciclo de sincronização de todas as lojas a cada <see cref="SyncOptions.EffectiveInterval"/>.</summary>
internal sealed class SyncWorker(
    OrderSyncEngine engine,
    IOrderStore store,
    IOptions<SyncOptions> syncOptions,
    IOptions<List<StoreOptions>> storesOptions,
    ILogger<SyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await store.InitializeAsync(stoppingToken).ConfigureAwait(false);

        var stores = storesOptions.Value.Where(s => s.Enabled).ToList();
        var interval = syncOptions.Value.EffectiveInterval;
        logger.LogInformation("Conector A7 → Foody iniciado: {Count} loja(s), ciclo de {Interval}",
            stores.Count, interval);

        using var timer = new PeriodicTimer(interval);
        do
        {
            foreach (var s in stores)
            {
                try
                {
                    await engine.RunOnceAsync(s, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
#pragma warning disable CA1031 // erro em uma loja não pode parar as outras nem derrubar o serviço
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    logger.LogError(ex, "Ciclo da loja {Store} falhou; tenta de novo no próximo ciclo", s.DisplayName);
                }
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
