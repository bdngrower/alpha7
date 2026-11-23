using System.Net;
using System.Text.Json;
using A7Foody.Core.Mapping;
using A7Foody.Core.Sales;
using A7Foody.Foody;
using A7Foody.Foody.Json;
using A7Foody.Foody.Models;
using Microsoft.Extensions.Logging;

namespace A7Foody.Core.Sync;

/// <summary>
/// Um ciclo de sincronização de uma loja:
/// 1) lê vendas novas/alteradas do A7 e grava na outbox;
/// 2) envia criações e cancelamentos pendentes à Foody;
/// 3) consulta status das entregas em andamento (polling) e repassa mudanças.
/// Cada etapa é tolerante a falha: o que não deu certo fica gravado e é retentado no próximo ciclo.
/// </summary>
public sealed class OrderSyncEngine(
    ISalesSource source,
    IOrderStore store,
    IFoodyClientFactory foodyFactory,
    IDeliveryStatusSink statusSink,
    FoodyOrderMapper mapper,
    SyncOptions options,
    TimeProvider clock,
    ILogger<OrderSyncEngine> logger)
{
    private static readonly SyncState[] OutboxStates = [SyncState.PendingCreate, SyncState.CreateUncertain, SyncState.PendingCancel];
    private static readonly SyncState[] TrackingStates = [SyncState.Tracking];

    /// <summary>Executa um ciclo completo para a loja.</summary>
    public async Task RunOnceAsync(StoreOptions storeConfig, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(storeConfig);
        var foody = foodyFactory.ForToken(storeConfig.FoodyToken);

        await IngestAsync(storeConfig, ct).ConfigureAwait(false);
        await ProcessOutboxAsync(storeConfig, foody, ct).ConfigureAwait(false);
        await PollStatusesAsync(storeConfig, foody, ct).ConfigureAwait(false);
    }

    // ---------------------------------------------------------------- 1) A7 → outbox

    internal async Task IngestAsync(StoreOptions storeConfig, CancellationToken ct)
    {
        var cursor = await store.GetCursorAsync(storeConfig.StoreId, ct).ConfigureAwait(false);
        var batch = await source.GetChangesAsync(storeConfig.StoreId, cursor, ct).ConfigureAwait(false);

        foreach (var sale in batch.Sales)
        {
            await IngestSaleAsync(sale, ct).ConfigureAwait(false);
        }

        // O cursor só avança depois que todas as vendas do lote estão gravadas.
        if (batch.NextCursor != cursor)
        {
            await store.SetCursorAsync(storeConfig.StoreId, batch.NextCursor, ct).ConfigureAwait(false);
        }
    }

    private async Task IngestSaleAsync(DeliverySale sale, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var existing = await store.GetAsync(sale.StoreId, sale.SaleId, ct).ConfigureAwait(false);

        if (sale.State == SaleState.Cancelled)
        {
            await IngestCancellationAsync(sale, existing, now, ct).ConfigureAwait(false);
            return;
        }

        // Venda ativa: só (re)monta o pedido enquanto ele ainda não saiu para a Foody. Isso também permite
        // corrigir um cadastro inválido no A7 e o pedido seguir sozinho no próximo ciclo.
        if (existing is not null && existing.State is not (SyncState.PendingCreate or SyncState.NeedsAttention))
        {
            return;
        }

        if (existing is { State: SyncState.NeedsAttention, FoodyUid: not null })
        {
            return; // já existe na Foody; a pendência é outra (ex.: cancelamento após coleta)
        }

        var mapped = mapper.Map(sale);
        var payload = mapped.Order is null ? null : JsonSerializer.Serialize(mapped.Order, FoodyJson.Options);
        var errors = mapped.IsValid ? null : string.Join("; ", mapped.Errors);

        // Mesma venda lida de novo sem mudança no que seria enviado: nada a fazer. Isso preserva o backoff
        // de um envio pendente e impede que um pedido recusado pela Foody (400) seja reenviado sem correção.
        if (existing is not null && existing.PayloadJson == payload && (payload is not null || existing.LastError == errors))
        {
            return;
        }

        // Pendente que mudou (ex.: endereço corrigido antes do envio): troca o conteúdo, mantém o backoff.
        var keepBackoff = existing is { State: SyncState.PendingCreate } && mapped.IsValid;
        var order = new TrackedOrder
        {
            StoreId = sale.StoreId,
            SaleId = sale.SaleId,
            SaleCreatedAt = sale.CreatedAt,
            FoodyOrderId = mapped.Order?.Id ?? mapper.BuildOrderId(sale),
            State = mapped.IsValid ? SyncState.PendingCreate : SyncState.NeedsAttention,
            PayloadJson = payload,
            LastError = keepBackoff ? existing!.LastError : errors,
            Attempts = keepBackoff ? existing!.Attempts : 0,
            NextAttemptAt = keepBackoff ? existing!.NextAttemptAt : now,
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now,
        };

        if (!mapped.IsValid)
        {
            logger.LogWarning("Venda {Store}/{Sale} com cadastro inválido, não enviada: {Errors}",
                sale.StoreId, sale.SaleId, order.LastError);
        }

        await store.SaveAsync(order, ct).ConfigureAwait(false);
    }

    private async Task IngestCancellationAsync(DeliverySale sale, TrackedOrder? existing, DateTimeOffset now, CancellationToken ct)
    {
        TrackedOrder? updated = existing switch
        {
            null => null, // nunca foi enviada: nada a fazer
            { State: SyncState.Done } or { State: SyncState.PendingCancel } => null,
            { State: SyncState.PendingCreate } or { State: SyncState.NeedsAttention, FoodyUid: null } =>
                existing with { State = SyncState.Done, LastError = "Cancelada no A7 antes do envio.", UpdatedAt = now },
            { State: SyncState.CreateUncertain } =>
                existing with { CancelRequested = true, UpdatedAt = now },
            _ => existing with { State = SyncState.PendingCancel, Attempts = 0, NextAttemptAt = now, UpdatedAt = now },
        };

        if (updated is not null)
        {
            await store.SaveAsync(updated, ct).ConfigureAwait(false);
        }
    }

    // ---------------------------------------------------------------- 2) outbox → Foody

    internal async Task ProcessOutboxAsync(StoreOptions storeConfig, IFoodyClient foody, CancellationToken ct)
    {
        var due = await store.GetDueAsync(storeConfig.StoreId, OutboxStates, clock.GetUtcNow(), options.BatchSize, ct).ConfigureAwait(false);
        foreach (var order in due)
        {
            ct.ThrowIfCancellationRequested();
            var result = order.State switch
            {
                SyncState.PendingCreate => await CreateAsync(order, foody, ct).ConfigureAwait(false),
                SyncState.CreateUncertain => await ReconcileUncertainAsync(order, foody, ct).ConfigureAwait(false),
                SyncState.PendingCancel => await CancelAsync(order, foody, ct).ConfigureAwait(false),
                _ => order,
            };
            await store.SaveAsync(result with { UpdatedAt = clock.GetUtcNow() }, ct).ConfigureAwait(false);
        }
    }

    private async Task<TrackedOrder> CreateAsync(TrackedOrder order, IFoodyClient foody, CancellationToken ct)
    {
        var request = JsonSerializer.Deserialize<FoodyOrderRequest>(order.PayloadJson!, FoodyJson.Options)!;
        try
        {
            var created = await foody.CreateOrderAsync(request, ct).ConfigureAwait(false);
            logger.LogInformation("Pedido {Store}/{Sale} criado na Foody: uid {Uid}", order.StoreId, order.SaleId, created.Uid);
            return Created(order, created.Uid, created.Status);
        }
        catch (FoodyApiException ex) when (ex.IsDataError)
        {
            logger.LogWarning("Foody recusou {Store}/{Sale}: {Error}", order.StoreId, order.SaleId, ex.Message);
            return order with { State = SyncState.NeedsAttention, LastError = ex.Message };
        }
        catch (FoodyApiException ex) when (ex.StatusCode is HttpStatusCode.TooManyRequests
                                           or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            // Requisição recusada antes de processar: seguro reenviar depois.
            // 401/403 = token errado, plano Free (E11) ou limite (E04): fica tentando e registrando no log.
            logger.LogWarning("Envio de {Store}/{Sale} recusado ({Error}); nova tentativa mais tarde",
                order.StoreId, order.SaleId, ex.Message);
            return Backoff(order, ex.Message);
        }
        catch (Exception ex) when (IsAmbiguous(ex, ct))
        {
            // Não sabemos se a Foody criou o pedido. Próximo passo: procurar antes de reenviar.
            logger.LogWarning(ex, "Resultado incerto ao enviar {Store}/{Sale}; vai conferir na Foody antes de reenviar",
                order.StoreId, order.SaleId);
            return Backoff(order with { State = SyncState.CreateUncertain }, ex.Message);
        }
    }

    private async Task<TrackedOrder> ReconcileUncertainAsync(TrackedOrder order, IFoodyClient foody, CancellationToken ct)
    {
        try
        {
            var from = order.SaleCreatedAt.AddHours(-1);
            var to = clock.GetUtcNow().AddMinutes(5);
            var candidates = await foody.ListOrdersAsync(from, to, ct).ConfigureAwait(false);
            var match = candidates.FirstOrDefault(o => string.Equals(o.Id, order.FoodyOrderId, StringComparison.Ordinal));

            if (match is not null)
            {
                logger.LogInformation("Pedido {Store}/{Sale} já existia na Foody (uid {Uid}); não será reenviado",
                    order.StoreId, order.SaleId, match.Uid);
                return Created(order, match.Uid, match.Status);
            }

            if (order.CancelRequested)
            {
                return order with { State = SyncState.Done, LastError = "Cancelada no A7; pedido não chegou a ser criado na Foody." };
            }

            return order with { State = SyncState.PendingCreate, NextAttemptAt = clock.GetUtcNow() };
        }
        catch (Exception ex) when (ex is FoodyApiException or HttpRequestException || IsAmbiguous(ex, ct))
        {
            return Backoff(order, "Conferência na Foody falhou: " + ex.Message);
        }
    }

    private TrackedOrder Created(TrackedOrder order, string uid, FoodyOrderStatus status)
    {
        var tracked = order with
        {
            FoodyUid = uid,
            FoodyStatus = status,
            State = SyncState.Tracking,
            Attempts = 0,
            LastError = null,
            NextAttemptAt = clock.GetUtcNow(),
        };

        // Cancelada no A7 enquanto o envio estava incerto: agora que temos o uid, cancela.
        return order.CancelRequested ? tracked with { State = SyncState.PendingCancel } : tracked;
    }

    private async Task<TrackedOrder> CancelAsync(TrackedOrder order, IFoodyClient foody, CancellationToken ct)
    {
        try
        {
            await foody.UpdateStatusAsync(order.FoodyUid!, FoodyOrderStatus.Cancelled, ct).ConfigureAwait(false);
            logger.LogInformation("Pedido {Store}/{Sale} cancelado na Foody", order.StoreId, order.SaleId);
            return order with { State = SyncState.Done, FoodyStatus = FoodyOrderStatus.Cancelled, LastError = null };
        }
        catch (FoodyApiException ex) when (ex.ErrorCode == "E25" || ex.IsDataError || ex.StatusCode == HttpStatusCode.NotFound)
        {
            // E25: pedido já coletado — a Foody exige motivos que só uma pessoa pode informar.
            logger.LogWarning("Cancelamento de {Store}/{Sale} precisa ser feito no portal Foody: {Error}",
                order.StoreId, order.SaleId, ex.Message);
            return order with { State = SyncState.NeedsAttention, LastError = "Cancelar manualmente no portal Foody: " + ex.Message };
        }
        catch (Exception ex) when (ex is FoodyApiException or HttpRequestException || IsAmbiguous(ex, ct))
        {
            // Cancelar é idempotente: pode repetir sem medo.
            return Backoff(order, ex.Message);
        }
    }

    // ---------------------------------------------------------------- 3) Foody → status

    internal async Task PollStatusesAsync(StoreOptions storeConfig, IFoodyClient foody, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var all = await store.GetDueAsync(storeConfig.StoreId, TrackingStates, now, int.MaxValue, ct).ConfigureAwait(false);

        // Entregas que nunca terminam (esquecidas no portal, por exemplo) param de ser consultadas.
        var tracking = new List<TrackedOrder>(all.Count);
        foreach (var order in all)
        {
            if (now - order.SaleCreatedAt > options.TrackingMaxAge)
            {
                await store.SaveAsync(order with
                {
                    State = SyncState.NeedsAttention,
                    LastError = $"Sem conclusão na Foody após {options.TrackingMaxAge.TotalHours:0} h (último status: {order.FoodyStatus}).",
                    UpdatedAt = now,
                }, ct).ConfigureAwait(false);
            }
            else
            {
                tracking.Add(order);
            }
        }

        foreach (var chunk in tracking.Chunk(Math.Max(1, options.CheckUpdatesBatchSize)))
        {
            IReadOnlyList<FoodyOrderUpdate> updates;
            try
            {
                updates = await foody.CheckUpdatesAsync(chunk.Select(o => o.FoodyUid!), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is FoodyApiException or HttpRequestException || IsAmbiguous(ex, ct))
            {
                logger.LogWarning(ex, "Falha no checkupdates da loja {Store}; tenta no próximo ciclo", storeConfig.DisplayName);
                return;
            }

            var byUid = chunk.ToDictionary(o => o.FoodyUid!, StringComparer.Ordinal);
            foreach (var update in updates)
            {
                if (byUid.TryGetValue(update.Uid, out var order) && update.Status != FoodyOrderStatus.Unknown)
                {
                    await ApplyUpdateAsync(order, update, foody, ct).ConfigureAwait(false);
                }
            }
        }
    }

    private async Task ApplyUpdateAsync(TrackedOrder order, FoodyOrderUpdate update, IFoodyClient foody, CancellationToken ct)
    {
        var current = order;
        if (update.Status != order.FoodyStatus)
        {
            logger.LogInformation("Pedido {Store}/{Sale}: {From} → {To}", order.StoreId, order.SaleId, order.FoodyStatus, update.Status);
            await NotifyAsync(order, order.FoodyStatus, update.Status, update.UpdateDate, ct).ConfigureAwait(false);
            current = order with { FoodyStatus = update.Status };
        }

        if (current.FoodyStatus == FoodyOrderStatus.Delivered && options.AutoCloseDelivered)
        {
            try
            {
                await foody.UpdateStatusAsync(current.FoodyUid!, FoodyOrderStatus.Closed, ct).ConfigureAwait(false);
                await NotifyAsync(current, FoodyOrderStatus.Delivered, FoodyOrderStatus.Closed, clock.GetUtcNow(), ct).ConfigureAwait(false);
                current = current with { FoodyStatus = FoodyOrderStatus.Closed };
            }
            catch (Exception ex) when (ex is FoodyApiException or HttpRequestException || IsAmbiguous(ex, ct))
            {
                logger.LogWarning(ex, "Não foi possível fechar {Store}/{Sale}; tenta no próximo ciclo", order.StoreId, order.SaleId);
            }
        }

        if (current.FoodyStatus.IsFinal())
        {
            current = current with { State = SyncState.Done };
        }

        if (current != order)
        {
            await store.SaveAsync(current with { UpdatedAt = clock.GetUtcNow() }, ct).ConfigureAwait(false);
        }
    }

    private async Task NotifyAsync(TrackedOrder order, FoodyOrderStatus from, FoodyOrderStatus to, DateTimeOffset? at, CancellationToken ct)
    {
        try
        {
            await statusSink.OnStatusChangedAsync(
                new DeliveryStatusChange(order.StoreId, order.SaleId, order.FoodyUid!, from, to, at), ct).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // falha no retorno ao A7 não pode travar o acompanhamento da entrega
        catch (Exception ex) when (!ct.IsCancellationRequested)
#pragma warning restore CA1031
        {
            logger.LogError(ex, "Falha ao repassar status de {Store}/{Sale}", order.StoreId, order.SaleId);
        }
    }

    // ---------------------------------------------------------------- utilitários

    private TrackedOrder Backoff(TrackedOrder order, string error)
    {
        var attempts = order.Attempts + 1;
        var factor = Math.Pow(2, Math.Min(attempts - 1, 16));
        var delay = TimeSpan.FromTicks((long)Math.Min(options.RetryBaseDelay.Ticks * factor, options.RetryMaxDelay.Ticks));
        return order with { Attempts = attempts, LastError = error, NextAttemptAt = clock.GetUtcNow() + delay };
    }

    /// <summary>Falhas em que a requisição pode ou não ter sido processada pela Foody.</summary>
    private static bool IsAmbiguous(Exception ex, CancellationToken ct) => ex switch
    {
        OperationCanceledException when ct.IsCancellationRequested => false, // desligando o serviço
        FoodyApiException api => (int)api.StatusCode >= 500 || api.StatusCode == HttpStatusCode.RequestTimeout,
        HttpRequestException or TimeoutException or OperationCanceledException => true,
        _ => false,
    };
}
