using A7Foody.Foody.Models;

namespace A7Foody.Core.Sync;

/// <summary>
/// Estado de uma venda no conector (tabela <c>orders</c> do banco local). É a "outbox": o que precisa ser
/// enviado à Foody fica gravado aqui antes de qualquer chamada, então nada se perde se o serviço cair.
/// </summary>
public sealed record TrackedOrder
{
    /// <summary>Loja.</summary>
    public required string StoreId { get; init; }

    /// <summary>Venda no A7Pharma.</summary>
    public required string SaleId { get; init; }

    /// <summary>Id enviado à Foody (campo id, ≤ 10 caracteres).</summary>
    public string? FoodyOrderId { get; init; }

    /// <summary>UID devolvido pela Foody após a criação.</summary>
    public string? FoodyUid { get; init; }

    /// <summary>Etapa no conector.</summary>
    public required SyncState State { get; init; }

    /// <summary>Último status conhecido na Foody.</summary>
    public FoodyOrderStatus FoodyStatus { get; init; }

    /// <summary>Data da venda (usada para procurar o pedido na Foody em caso de dúvida).</summary>
    public required DateTimeOffset SaleCreatedAt { get; init; }

    /// <summary>JSON do pedido a enviar. Guardado para que uma nova tentativa envie exatamente o mesmo conteúdo.</summary>
    public string? PayloadJson { get; init; }

    /// <summary>Tentativas sem sucesso na etapa atual.</summary>
    public int Attempts { get; init; }

    /// <summary>Não tentar antes deste momento (backoff).</summary>
    public DateTimeOffset NextAttemptAt { get; init; }

    /// <summary>Último erro, para diagnóstico.</summary>
    public string? LastError { get; init; }

    /// <summary>Venda foi cancelada no A7 enquanto o envio estava em andamento.</summary>
    public bool CancelRequested { get; init; }

    /// <summary>Criação do registro.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Última alteração.</summary>
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>Etapas do ciclo de vida no conector.</summary>
public enum SyncState
{
    /// <summary>Aguardando envio (POST /orders).</summary>
    PendingCreate = 0,

    /// <summary>
    /// O POST falhou de um jeito que não dá para saber se o pedido foi criado (timeout, queda de conexão,
    /// erro 5xx). Antes de reenviar, o conector procura o pedido na Foody para não duplicar a entrega.
    /// </summary>
    CreateUncertain = 1,

    /// <summary>Criado na Foody; acompanhando status por polling.</summary>
    Tracking = 2,

    /// <summary>Cancelado no A7; aguardando envio do cancelamento à Foody.</summary>
    PendingCancel = 3,

    /// <summary>Ciclo encerrado (closed, cancelled, rejected ou cancelado antes do envio).</summary>
    Done = 4,

    /// <summary>Problema que precisa de pessoa (cadastro inválido, cancelamento após coleta...). Ver LastError.</summary>
    NeedsAttention = 5,
}
